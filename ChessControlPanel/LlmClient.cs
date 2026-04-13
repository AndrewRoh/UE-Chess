using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ChessControlPanel;

/// <summary>
/// Ollama API를 통한 LLM 호출 클라이언트.
/// chess-api의 llm_client.py 기능을 C#으로 이관.
/// </summary>
public sealed class LlmClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string     _ollamaHost;
    private readonly string     _model;

    public LlmClient(
        string ollamaHost = "http://127.0.0.1:11434",
        string model      = "gemma4:e4b")
    {
        _ollamaHost = ollamaHost;
        _model      = model;
        _http       = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    }

    /// <summary>Ollama /api/generate 호출 — 비스트리밍, JSON 응답 문자열 반환</summary>
    public async Task<string> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        double temperature = 0.3,
        CancellationToken ct = default)
    {
        var payload = new
        {
            model  = _model,
            system = systemPrompt,
            prompt = userPrompt,
            stream = false,
            options = new
            {
                temperature,
                num_predict    = 500,
                top_p          = 0.9,
                repeat_penalty = 1.1,
                num_gpu        = 99,
            },
            format = "json",
        };

        string body = JsonSerializer.Serialize(payload);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{_ollamaHost}/api/generate", content, ct);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("response").GetString() ?? "";
    }

    /// <summary>Ollama 서버가 살아있는지 확인 (/api/tags 엔드포인트로 체크)</summary>
    public async Task<bool> IsAliveAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var resp = await _http.GetAsync($"{_ollamaHost}/api/tags", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void Dispose() => _http.Dispose();
}
