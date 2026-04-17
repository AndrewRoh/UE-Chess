namespace ChessControlPanel;

/// <summary>
/// AI 실력 레벨 — 프롬프트 전략, temperature, 사고 깊이에 영향.
/// </summary>
public enum AiLevel
{
    /// <summary>초보 (ELO 800~1200) — 기본 규칙 위주, 단순 캡처/체크 선호, 실수 허용</summary>
    Beginner = 1,

    /// <summary>중급 (ELO 1400~1800) — 전술 패턴 인식, 기본 전략 이해, 블런더 최소화</summary>
    Intermediate = 2,

    /// <summary>고급 (ELO 2000+) — 깊은 계산, 전략적 판단, 엔드게임 기술</summary>
    Expert = 3,
}

/// <summary>
/// 레벨별 LLM 호출 파라미터 프로필.
/// </summary>
public readonly record struct LevelProfile(
    double Temperature,
    int    NumPredict,
    double TopP,
    double RepeatPenalty,
    string SystemPromptFile,
    string MoveTemplateFile)
{
    public static LevelProfile For(AiLevel level, string phase) => level switch
    {
        // 초보: 높은 temperature로 다양성↑, 최선수가 아닌 합리적인 수 선택
        AiLevel.Beginner => new(
            Temperature:     phase == "Opening" ? 0.8 : 0.7,
            NumPredict:      120,
            TopP:            0.95,
            RepeatPenalty:   1.05,
            SystemPromptFile: "chess_system_beginner.txt",
            MoveTemplateFile: "move_generation_beginner.txt"),

        // 중급: 균형잡힌 설정, 전술 우선
        AiLevel.Intermediate => new(
            Temperature:     phase switch { "Opening" => 0.5, "Endgame" => 0.3, _ => 0.4 },
            NumPredict:      180,
            TopP:            0.9,
            RepeatPenalty:   1.1,
            SystemPromptFile: "chess_system_intermediate.txt",
            MoveTemplateFile: "move_generation_intermediate.txt"),

        // 고급: 낮은 temperature로 일관된 최선수, 긴 추론
        AiLevel.Expert => new(
            Temperature:     phase switch { "Opening" => 0.3, "Endgame" => 0.1, _ => 0.2 },
            NumPredict:      300,
            TopP:            0.85,
            RepeatPenalty:   1.15,
            SystemPromptFile: "chess_system_expert.txt",
            MoveTemplateFile: "move_generation_expert.txt"),

        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "알 수 없는 AI 레벨"),
    };
}
