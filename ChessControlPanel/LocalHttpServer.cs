using System.Net;
using System.Text;
using System.Text.Json;

namespace ChessControlPanel;

/// <summary>
/// 포트 18080에서 UE5의 AI 수 요청을 수신하는 인라인 HTTP 서버.
/// chess-api(Python FastAPI)의 /api/ue/ai_move 엔드포인트를 대체.
/// </summary>
public sealed class LocalHttpServer : IDisposable
{
    private readonly MoveGenerationService _moveService;
    private readonly HttpListener          _listener = new();
    private          Task?                 _listenTask;
    private volatile bool                  _running;

    public LocalHttpServer(MoveGenerationService moveService)
    {
        _moveService = moveService;
        _listener.Prefixes.Add("http://127.0.0.1:18080/");
    }

    public void Start()
    {
        _listener.Start();
        _running    = true;
        _listenTask = Task.Run(ListenLoopAsync);
    }

    public void Stop()
    {
        _running = false;
        try { _listener.Stop(); } catch { }
    }

    private async Task ListenLoopAsync()
    {
        while (_running)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleAsync(ctx);          // fire-and-forget, 각 요청 병렬 처리
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        string path   = ctx.Request.Url?.AbsolutePath ?? "";
        string method = ctx.Request.HttpMethod;

        try
        {
            if (path == "/api/ue/ai_move" && method == "POST")
            {
                await HandleAiMoveAsync(ctx);
            }
            else if (path == "/api/health" && method == "GET")
            {
                await WriteJsonAsync(ctx, 200, new { status = "ok", service = "ChessControlPanel" });
            }
            else
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }
        catch (Exception ex)
        {
            try { await WriteJsonAsync(ctx, 500, new { ok = false, error = ex.Message }); }
            catch { }
        }
    }

    private async Task HandleAiMoveAsync(HttpListenerContext ctx)
    {
        using var reader = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        string body = await reader.ReadToEndAsync();

        var req = JsonSerializer.Deserialize<UEAiMoveRequest>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (req == null || req.LegalMoves == null || req.LegalMoves.Length == 0)
        {
            await WriteJsonAsync(ctx, 400, new { ok = false, error = "legalMoves가 비어 있습니다" });
            return;
        }

        AiMoveResponse result = await _moveService.SelectMoveAsync(req);
        await WriteJsonAsync(ctx, 200, result);
    }

    private static async Task WriteJsonAsync(HttpListenerContext ctx, int status, object data)
    {
        string json  = JsonSerializer.Serialize(data);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        ctx.Response.StatusCode      = status;
        ctx.Response.ContentType     = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        Stop();
        (_listener as IDisposable)?.Dispose();
    }
}
