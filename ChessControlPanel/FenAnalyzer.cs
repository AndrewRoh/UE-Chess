namespace ChessControlPanel;

/// <summary>
/// FEN 문자열에서 재료 균형 및 체크 상태 등을 분석.
/// LLM 프롬프트에 주입할 보조 정보 계산용 (경량 구현).
/// </summary>
public static class FenAnalyzer
{
    // 표준 체스 말 가치 (폰=1, 나이트/비숍=3, 룩=5, 퀸=9, 킹=0 — 비교 무의미)
    private static readonly Dictionary<char, int> PieceValues = new()
    {
        ['P'] = 1, ['N'] = 3, ['B'] = 3, ['R'] = 5, ['Q'] = 9, ['K'] = 0,
        ['p'] = 1, ['n'] = 3, ['b'] = 3, ['r'] = 5, ['q'] = 9, ['k'] = 0,
    };

    /// <summary>
    /// FEN의 piece placement 필드에서 백/흑 재료를 합산하여 차이 표기.
    /// 예: "White +2" (백이 2점 우세), "Equal", "Black +1"
    /// </summary>
    public static string CalculateMaterialBalance(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return "unknown";

        string placement = fen.Split(' ')[0];
        int white = 0, black = 0;

        foreach (char c in placement)
        {
            if (!PieceValues.TryGetValue(c, out int v)) continue;
            if (char.IsUpper(c)) white += v;
            else                 black += v;
        }

        int diff = white - black;
        return diff switch
        {
            0     => "Equal",
            > 0   => $"White +{diff}",
            _     => $"Black +{-diff}",
        };
    }

    /// <summary>
    /// 체크 상태 감지 — 합법 수에 '+'나 '#'이 섞여 있지 않아도
    /// 합법 수 중 킹 이동이 비정상적으로 많거나 제한적이면 체크 가능성 시사.
    /// 신뢰 가능한 감지는 별도 체스 엔진이 필요하므로, 여기서는 LegalMoves의 suffix로만 판단.
    /// </summary>
    public static bool IsKingInCheck(string fen, string[] legalMoves)
    {
        // UCI 표기에는 +/# 가 없으므로, 별도 체크 판정이 필요.
        // 외부(UE) 쪽에서 FEN에 체크 상태 플래그를 넣거나, LegalMoves 필터링으로 알려주지 않는 이상
        // 단순 FEN만으로 정확한 감지는 어려움 — 보수적으로 false 반환.
        // 향후 체스 엔진 연동 시 이 메서드를 교체.
        _ = fen; _ = legalMoves;
        return false;
    }
}
