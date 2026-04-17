using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ChessControlPanel;

/// <summary>
/// FEN + 합법 수(UCI) 목록을 받아 LLM으로 최선의 수 1개를 선택.
/// 레벨별 프롬프트/파라미터 차별화, 흑/백 독립 히스토리, 체크 경고, 재료 균형 계산 지원.
/// </summary>
public sealed class MoveGenerationService
{
    private readonly LlmClient _llm;
    private readonly string    _promptDir;

    // 레벨별 프롬프트 캐시 — 파일 I/O를 최초 1회로 제한.
    private readonly Dictionary<AiLevel, (string system, string template)> _promptCache = new();

    public MoveGenerationService(LlmClient llm, string? promptDir = null)
    {
        _llm = llm;
        _promptDir = promptDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prompts");
    }

    /// <summary>
    /// UE 요청을 받아 3회 재시도 후 폴백(랜덤)으로 수를 선택.
    /// </summary>
    public async Task<AiMoveResponse> SelectMoveAsync(UEAiMoveRequest req)
    {
        if (req.LegalMoves.Length == 0)
            return Error("합법 수 목록이 비어 있습니다");

        // ── 1. 레벨 결정 ─────────────────────────────────────
        var level = (AiLevel)(req.Level ?? (int)AiLevel.Intermediate);
        if (!Enum.IsDefined(typeof(AiLevel), level)) level = AiLevel.Intermediate;

        // ── 2. 현재 차례 확정 (명시값 > FEN 파싱 > history 길이 추정) ─
        string side = ResolveSideToMove(req);

        // ── 3. 흑/백 독립 히스토리 분리 ──────────────────────
        var history = SideHistory.FromRequest(req);
        var (ownHist, oppHist) = history.FormatForSide(side);

        // ── 4. 국면(Opening/Middlegame/Endgame) 판정 ─────────
        int totalMoves = history.TotalMoves;
        string phase = totalMoves switch
        {
            < 10 => "Opening",
            < 30 => "Middlegame",
            _    => "Endgame",
        };

        // ── 5. 레벨별 프로필 로드 ────────────────────────────
        var profile = LevelProfile.For(level, phase);
        var (systemPrompt, moveTemplate) = LoadPrompts(level, profile);

        // ── 6. 프롬프트 변수 치환 ────────────────────────────
        string legalMovesStr = string.Join(", ", req.LegalMoves);
        string materialBalance = FenAnalyzer.CalculateMaterialBalance(req.Fen);
        string checkWarning    = FenAnalyzer.IsKingInCheck(req.Fen, req.LegalMoves)
            ? "\n[경고] 현재 체크 상태입니다 — 체크 회피가 최우선입니다."
            : "";

        string userPrompt = moveTemplate
            .Replace("{fen}",              req.Fen)
            .Replace("{side_to_move}",     side == "w" ? "White" : "Black")
            .Replace("{own_history}",      ownHist)
            .Replace("{opponent_history}", oppHist)
            .Replace("{move_history}",     FormatFullHistory(history))  // 하위 호환
            .Replace("{legal_moves}",      legalMovesStr)
            .Replace("{legal_move_count}", req.LegalMoves.Length.ToString())
            .Replace("{game_phase}",       phase)
            .Replace("{material_balance}", materialBalance)
            .Replace("{check_warning}",    checkWarning)
            .Replace("{{", "{")
            .Replace("}}", "}");

        // ── 7. 3회 재시도 루프 (전체 100초 타임아웃) ─────────
        using var globalCts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var stopwatch = Stopwatch.StartNew();
        string? lastError = null;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (globalCts.Token.IsCancellationRequested) break;

            try
            {
                string raw = await _llm.GenerateAsync(systemPrompt, userPrompt, profile, globalCts.Token);

                using var doc  = JsonDocument.Parse(raw);
                string    move = doc.RootElement.TryGetProperty("move", out var mv)
                               ? (mv.GetString() ?? "")
                               : "";

                if (Array.IndexOf(req.LegalMoves, move) >= 0)
                {
                    stopwatch.Stop();
                    return new AiMoveResponse
                    {
                        Ok         = true,
                        MoveUci    = move,
                        Candidates = GetStringArray(doc.RootElement, "candidates"),
                        Evaluation = GetString(doc.RootElement,      "evaluation"),
                        CommentKo  = GetString(doc.RootElement,      "comment"),
                        LevelUsed  = (int)level,
                        LatencyMs  = stopwatch.ElapsedMilliseconds,
                        IsFallback = false,
                    };
                }

                lastError = $"Attempt {attempt}: 목록에 없는 수 반환됨 ('{move}')";
            }
            catch (OperationCanceledException)
            {
                lastError = "전체 타임아웃(100초) 초과";
                break;
            }
            catch (JsonException ex)
            {
                lastError = $"Attempt {attempt}: JSON 파싱 실패 — {ex.Message}";
                if (attempt == 3) break;
            }
            catch (Exception ex)
            {
                lastError = $"Attempt {attempt}: {ex.GetType().Name} — {ex.Message}";
                if (attempt == 3 || globalCts.Token.IsCancellationRequested) break;
            }
        }

        // ── 8. 폴백: 합법 수 중 랜덤 ────────────────────────
        stopwatch.Stop();
        string fallback = req.LegalMoves[Random.Shared.Next(req.LegalMoves.Length)];
        return new AiMoveResponse
        {
            Ok         = true,
            MoveUci    = fallback,
            CommentKo  = "자동 선택된 수입니다.",
            Error      = $"LLM 호출 실패 — 랜덤 수 선택 ({lastError})",
            LevelUsed  = (int)level,
            LatencyMs  = stopwatch.ElapsedMilliseconds,
            IsFallback = true,
        };
    }

    // ── 프롬프트 로딩 (캐시) ──────────────────────────────────
    private (string system, string template) LoadPrompts(AiLevel level, LevelProfile profile)
    {
        if (_promptCache.TryGetValue(level, out var cached)) return cached;

        string sysPath  = Path.Combine(_promptDir, profile.SystemPromptFile);
        string tmplPath = Path.Combine(_promptDir, profile.MoveTemplateFile);

        // 레벨별 파일이 없으면 기본 파일로 폴백 (하위 호환)
        if (!File.Exists(sysPath))  sysPath  = Path.Combine(_promptDir, "chess_system.txt");
        if (!File.Exists(tmplPath)) tmplPath = Path.Combine(_promptDir, "move_generation.txt");

        var entry = (
            File.ReadAllText(sysPath,  System.Text.Encoding.UTF8),
            File.ReadAllText(tmplPath, System.Text.Encoding.UTF8));
        _promptCache[level] = entry;
        return entry;
    }

    // ── 차례 결정 ─────────────────────────────────────────────
    private static string ResolveSideToMove(UEAiMoveRequest req)
    {
        // 1. 명시 제공
        if (!string.IsNullOrEmpty(req.SideToMove))
            return req.SideToMove.StartsWith("b") ? "b" : "w";

        // 2. FEN 파싱 (예: "... w KQkq - 0 1")
        if (!string.IsNullOrEmpty(req.Fen))
        {
            var parts = req.Fen.Split(' ');
            if (parts.Length >= 2 && (parts[1] == "w" || parts[1] == "b"))
                return parts[1];
        }

        // 3. 전체 history 길이로 추정 (짝수면 백 차례)
        int total = (req.WhiteHistory?.Length ?? 0) + (req.BlackHistory?.Length ?? 0);
        if (total == 0) total = req.MoveHistory?.Length ?? 0;
        return (total & 1) == 0 ? "w" : "b";
    }

    // ── 하위 호환용 전체 히스토리 포맷 ────────────────────────
    private static string FormatFullHistory(SideHistory h)
    {
        if (h.TotalMoves == 0) return "(none)";

        var merged = new List<string>(h.TotalMoves);
        int max = Math.Max(h.White.Count, h.Black.Count);
        for (int i = 0; i < max; i++)
        {
            if (i < h.White.Count) merged.Add(h.White[i]);
            if (i < h.Black.Count) merged.Add(h.Black[i]);
        }
        return string.Join(", ", merged);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────
    private static AiMoveResponse Error(string msg) =>
        new() { Ok = false, Error = msg };

    private static string GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string[] GetStringArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return arr.EnumerateArray()
                  .Select(x => x.GetString() ?? "")
                  .Where(s => !string.IsNullOrEmpty(s))
                  .ToArray();
    }
}
