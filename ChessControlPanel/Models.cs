using System.Text.Json.Serialization;

namespace ChessControlPanel;

/// <summary>UE5가 POST /api/ue/ai_move로 보내는 요청 본문</summary>
public class UEAiMoveRequest
{
    [JsonPropertyName("fen")]          public string   Fen          { get; set; } = "";
    [JsonPropertyName("legal_moves")]   public string[] LegalMoves  { get; set; } = [];
    [JsonPropertyName("move_history")]  public string[] MoveHistory { get; set; } = [];
    [JsonPropertyName("ai_level")]      public int      AiLevel     { get; set; } = 2;
    [JsonPropertyName("time_limit_ms")] public int      TimeLimitMs { get; set; } = 1500;
}

/// <summary>ChessControlPanel → UE5 응답</summary>
public class AiMoveResponse
{
    [JsonPropertyName("ok")]           public bool     Ok           { get; set; }
    [JsonPropertyName("moveUci")]      public string   MoveUci      { get; set; } = "";
    [JsonPropertyName("candidates")]   public string[] Candidates   { get; set; } = [];
    [JsonPropertyName("evaluation")]   public string   Evaluation   { get; set; } = "";
    [JsonPropertyName("commentKo")]    public string   CommentKo    { get; set; } = "";
    [JsonPropertyName("error")]        public string?  Error        { get; set; }
}
