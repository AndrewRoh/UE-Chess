namespace ChessControlPanel;

/// <summary>
/// UE → C# 패널로 전달되는 AI 수 요청.
/// </summary>
public sealed class UEAiMoveRequest
{
    /// <summary>현재 국면의 FEN 문자열</summary>
    public string   Fen         { get; init; } = "";

    /// <summary>현재 수를 둘 차례의 합법 수 목록 (UCI)</summary>
    public string[] LegalMoves  { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 전체 수순 기록 (UCI, 수 번호 순).
    /// 인덱스 0 = 1수째 백, 1 = 1수째 흑, 2 = 2수째 백 ...
    /// 하위 호환을 위해 유지 — 내부에서 흑/백으로 자동 분리됨.
    /// </summary>
    public string[]? MoveHistory { get; init; }

    /// <summary>현재 수를 둘 쪽 ("w" 또는 "b"). 생략 시 MoveHistory 길이로 추정.</summary>
    public string?  SideToMove  { get; init; }

    /// <summary>AI 실력 레벨 (1=초보, 2=중급, 3=고급). 기본값 = 중급.</summary>
    public int?     Level       { get; init; }

    /// <summary>수 선택 제한 시간 (ms). 현재 MoveGenerationService에서는 사용하지 않음 (100s 고정 타임아웃).</summary>
    public int?     TimeLimitMs { get; init; }

    /// <summary>
    /// (선택) 백의 수만 모아둔 기록. 제공되면 MoveHistory 파싱 대신 이 값을 사용.
    /// </summary>
    public string[]? WhiteHistory { get; init; }

    /// <summary>
    /// (선택) 흑의 수만 모아둔 기록. 제공되면 MoveHistory 파싱 대신 이 값을 사용.
    /// </summary>
    public string[]? BlackHistory { get; init; }
}

/// <summary>
/// C# → UE로 돌려주는 AI 수 응답.
/// </summary>
public sealed class AiMoveResponse
{
    public bool     Ok         { get; init; }
    public string?  MoveUci    { get; init; }
    public string[] Candidates { get; init; } = Array.Empty<string>();
    public string   Evaluation { get; init; } = "";
    public string   CommentKo  { get; init; } = "";
    public string?  Error      { get; init; }

    /// <summary>실제 사용된 AI 레벨 (디버깅용)</summary>
    public int?     LevelUsed  { get; init; }

    /// <summary>LLM 호출 소요 시간 (ms, 디버깅용)</summary>
    public long?    LatencyMs  { get; init; }

    /// <summary>폴백(랜덤) 선택 여부</summary>
    public bool     IsFallback { get; init; }
}

/// <summary>
/// 흑/백 독립 수순 히스토리.
/// </summary>
public sealed class SideHistory
{
    public List<string> White { get; } = new();
    public List<string> Black { get; } = new();

    /// <summary>전체 수순 (백↔흑 교차)으로부터 흑/백 히스토리를 분리 생성.</summary>
    public static SideHistory FromFullHistory(string[]? fullHistory)
    {
        var h = new SideHistory();
        if (fullHistory == null) return h;

        for (int i = 0; i < fullHistory.Length; i++)
        {
            // 인덱스 짝수 = 백, 홀수 = 흑 (체스 표준)
            if ((i & 1) == 0) h.White.Add(fullHistory[i]);
            else              h.Black.Add(fullHistory[i]);
        }
        return h;
    }

    /// <summary>요청에서 흑/백 히스토리 확정 (명시 제공 > MoveHistory 파싱).</summary>
    public static SideHistory FromRequest(UEAiMoveRequest req)
    {
        if (req.WhiteHistory != null || req.BlackHistory != null)
        {
            var h = new SideHistory();
            if (req.WhiteHistory != null) h.White.AddRange(req.WhiteHistory);
            if (req.BlackHistory != null) h.Black.AddRange(req.BlackHistory);
            return h;
        }
        return FromFullHistory(req.MoveHistory);
    }

    public int TotalMoves => White.Count + Black.Count;

    /// <summary>프롬프트용 포맷 — 자신 기록과 상대 기록을 분리 표기.</summary>
    public (string ownMoves, string opponentMoves) FormatForSide(string sideToMove)
    {
        bool isWhite = sideToMove == "w";
        var own = isWhite ? White : Black;
        var opp = isWhite ? Black : White;

        return (
            own.Count > 0 ? string.Join(", ", own) : "(none)",
            opp.Count > 0 ? string.Join(", ", opp) : "(none)"
        );
    }
}
