using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ChessControlPanel;

/// <summary>
/// Ollama API를 통한 LLM 호출 클라이언트.
/// 레벨별 샘플링 파라미터 지원 추가.
/// </summary>
public sealed class LlmClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string     _ollamaHost;
    private readonly string     _model;

    public string OllamaHost => _ollamaHost;
    public string Model      => _model;

    public LlmClient(
        string ollamaHost = "http://172.20.64.76:11434",
        string model      = "gemma4:latest",
        HttpClient? httpClient = null)
    {
        _ollamaHost = ollamaHost.TrimEnd('/');
        _model      = model;
        // DI/테스트 편의를 위해 외부 주입 허용. 미주입 시 내부 생성.
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    }

    /// <summary>
    /// Ollama /api/generate 호출 — 비스트리밍, JSON 응답 문자열 반환.
    /// </summary>
    /// <param name="profile">레벨별 샘플링 프로필 (temperature 등)</param>
    public async Task<string> GenerateAsync(
        string       systemPrompt,
        string       userPrompt,
        LevelProfile profile,
        CancellationToken ct = default)
    {
        var payload = new
        {
            model      = _model,
            system     = systemPrompt,
            prompt     = userPrompt,
            stream     = false,
            keep_alive = "30m",   // GPU 메모리에 30분 유지
            options = new
            {
                temperature    = profile.Temperature,
                num_predict    = profile.NumPredict,
                top_p          = profile.TopP,
                repeat_penalty = profile.RepeatPenalty,
                num_gpu        = 99,
            },
            format = "json",
        };

        string body = JsonSerializer.Serialize(payload);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync($"{_ollamaHost}/api/generate", content, ct);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("response").GetString() ?? "";
    }

    /// <summary>
    /// (하위 호환) 기존 코드에서 temperature만 넘기던 경우 지원.
    /// 신규 코드에서는 <see cref="GenerateAsync(string, string, LevelProfile, CancellationToken)"/> 사용.
    /// </summary>
    public Task<string> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        double temperature = 0.3,
        CancellationToken ct = default)
    {
        var legacy = new LevelProfile(
            Temperature:    temperature,
            NumPredict:     150,
            TopP:           0.9,
            RepeatPenalty:  1.1,
            SystemPromptFile: "",
            MoveTemplateFile: "");
        return GenerateAsync(systemPrompt, userPrompt, legacy, ct);
    }

    /// <summary>Ollama 서버가 살아있는지 확인 (/api/tags)</summary>
    public async Task<bool> IsAliveAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var resp = await _http.GetAsync($"{_ollamaHost}/api/tags", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void Dispose() => _http.Dispose();
}
