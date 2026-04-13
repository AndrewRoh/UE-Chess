using System.IO;
using System.Text.Json;

namespace ChessControlPanel;

/// <summary>
/// FEN + 합법 수(UCI) 목록을 받아 LLM으로 최선의 수 1개를 선택.
/// chess-api의 generate_ai_move / build_move_prompt 기능 이관.
/// </summary>
public sealed class MoveGenerationService
{
    private readonly LlmClient _llm;
    private readonly string    _systemPrompt;
    private readonly string    _moveTemplate;

    public MoveGenerationService(LlmClient llm)
    {
        _llm = llm;

        string promptDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prompts");
        _systemPrompt = File.ReadAllText(Path.Combine(promptDir, "chess_system.txt"), System.Text.Encoding.UTF8);
        _moveTemplate = File.ReadAllText(Path.Combine(promptDir, "move_generation.txt"), System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// UE 요청을 받아 3회 재시도 후 폴백(랜덤)으로 수를 선택.
    /// </summary>
    public async Task<AiMoveResponse> SelectMoveAsync(UEAiMoveRequest req)
    {
        if (req.LegalMoves.Length == 0)
            return Error("합법 수 목록이 비어 있습니다");

        int moveCount = req.MoveHistory?.Length ?? 0;
        (string phase, double temp) = moveCount switch
        {
            < 10 => ("Opening",    0.4),
            < 30 => ("Middlegame", 0.3),
            _    => ("Endgame",    0.2),
        };

        string legalMovesStr = string.Join(", ", req.LegalMoves);
        string historyStr    = moveCount > 0
            ? string.Join(", ", req.MoveHistory!)
            : "(none)";

        // Python str.format 이스케이프({{ → {)를 처리한 후 변수 치환
        string userPrompt = _moveTemplate
            .Replace("{fen}",             req.Fen)
            .Replace("{move_history}",    historyStr)
            .Replace("{legal_moves}",     legalMovesStr)
            .Replace("{game_phase}",      phase)
            .Replace("{material_balance}", "unknown")
            .Replace("{check_warning}",   "")
            .Replace("{{", "{")
            .Replace("}}", "}");

        // 3회 재시도 전체에 대한 단일 타임아웃 (100초).
        // 시도별로 90초를 따로 두면 최대 270초가 되어 UE5 HTTP 타임아웃(120초)을 초과할 수 있음.
        using var globalCts = new CancellationTokenSource(TimeSpan.FromSeconds(100));

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (globalCts.Token.IsCancellationRequested) break;

            try
            {
                string raw = await _llm.GenerateAsync(_systemPrompt, userPrompt, temp, globalCts.Token);

                using var doc  = JsonDocument.Parse(raw);
                string    move = doc.RootElement.GetProperty("move").GetString() ?? "";

                if (Array.IndexOf(req.LegalMoves, move) >= 0)
                {
                    return new AiMoveResponse
                    {
                        Ok         = true,
                        MoveUci    = move,
                        Candidates = GetStringArray(doc.RootElement, "candidates"),
                        Evaluation = GetString(doc.RootElement,      "evaluation"),
                        CommentKo  = GetString(doc.RootElement,      "comment"),
                        Error      = null,
                    };
                }

                // 목록에 없는 수 반환됨 → 재시도
            }
            catch (OperationCanceledException)
            {
                break; // 전체 타임아웃(100초) 초과
            }
            catch (Exception)
            {
                if (attempt == 3 || globalCts.Token.IsCancellationRequested) break;
            }
        }

        // 폴백: 합법 수 중 랜덤
        string fallback = req.LegalMoves[Random.Shared.Next(req.LegalMoves.Length)];
        return new AiMoveResponse
        {
            Ok        = true,
            MoveUci   = fallback,
            CommentKo = "자동 선택된 수입니다.",
            Error     = "LLM 호출 실패 — 랜덤 수 선택",
        };
    }

    // ── 헬퍼 ────────────────────────────────────────────────────
    private static AiMoveResponse Error(string msg) =>
        new() { Ok = false, Error = msg };

    private static string GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string[] GetStringArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return arr.EnumerateArray()
                  .Select(x => x.GetString() ?? "")
                  .ToArray();
    }
}
