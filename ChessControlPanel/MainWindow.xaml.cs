using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace ChessControlPanel;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    // ── 설정 ────────────────────────────────────────────────────
    private const string UeHost = "127.0.0.1";
    private const int    UePort = 7777;

    // ── 인프라 ───────────────────────────────────────────────────
    private readonly LlmClient             _llm;
    private readonly MoveGenerationService _moveService;

    // ── UE 연결 ──────────────────────────────────────────────────
    private TcpClient?     _client;
    private NetworkStream? _stream;
    private int            _aiLevel = 2;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // ── 상태 ────────────────────────────────────────────────────
    private bool   _isUeConnected;
    private bool   _isOllamaAlive;
    private string _errorText = string.Empty;

    // ── INotifyPropertyChanged ───────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string prop) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    public bool IsConnected
    {
        get => _isUeConnected;
        set { _isUeConnected = value; Notify(nameof(IsConnected)); Notify(nameof(UeStatusText)); }
    }

    public bool IsOllamaAlive
    {
        get => _isOllamaAlive;
        set { _isOllamaAlive = value; Notify(nameof(IsOllamaAlive)); Notify(nameof(OllamaStatusText)); }
    }

    public string UeStatusText =>
        _isUeConnected ? "● UE5 연결됨" : "● UE5 연결 안 됨";

    public string OllamaStatusText =>
        _isOllamaAlive ? "● Ollama 준비됨 (AI 대전 가능)" : "● Ollama 꺼짐 (AI 대전 불가)";

    public string ErrorText
    {
        get => _errorText;
        set { _errorText = value; Notify(nameof(ErrorText)); Notify(nameof(ErrorVisibility)); }
    }

    public Visibility ErrorVisibility =>
        string.IsNullOrEmpty(_errorText) ? Visibility.Collapsed : Visibility.Visible;

    public bool AiLevel1 => _aiLevel == 1;
    public bool AiLevel2 => _aiLevel == 2;
    public bool AiLevel3 => _aiLevel == 3;

    // ── 생성자 ───────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _llm         = new LlmClient();
        _moveService = new MoveGenerationService(_llm);

        StartReconnectLoop();
        StartOllamaCheckLoop();
    }

    // ── UE5 연결 루프 ────────────────────────────────────────────
    private void StartReconnectLoop()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                if (!IsSocketConnected())
                {
                    Dispatcher.Invoke(() => IsConnected = false);
                    await TryConnectAsync();
                }
                await Task.Delay(3000);
            }
        });
    }

    private bool IsSocketConnected()
    {
        try
        {
            return _client != null && _client.Connected &&
                   !(_client.Client.Poll(0, SelectMode.SelectRead) &&
                     _client.Client.Available == 0);
        }
        catch { return false; }
    }

    private async Task TryConnectAsync()
    {
        try
        {
            var c = new TcpClient();
            await c.ConnectAsync(UeHost, UePort);
            _client = c;
            _stream = c.GetStream();
            Dispatcher.Invoke(() => { IsConnected = true; ErrorText = string.Empty; });

            // 연결 성공 시 수신 루프 시작 (UE5 → WPF 메시지 처리)
            _ = Task.Run(() => ReceiveLoopAsync(_stream));
        }
        catch { /* 다음 루프에서 재시도 */ }
    }

    // ── UE5 → WPF 수신 루프 ─────────────────────────────────────
    private async Task ReceiveLoopAsync(NetworkStream stream)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            while (true)
            {
                string? line = await reader.ReadLineAsync();
                if (line == null) break; // 연결 종료

                if (!string.IsNullOrWhiteSpace(line))
                {
                    // LLM 처리가 오래 걸려도 수신 루프가 블로킹되지 않도록 별도 Task로 실행
                    string captured = line;
                    _ = Task.Run(() => HandleUeMessageAsync(captured));
                }
            }
        }
        catch { /* 연결 끊김 — StartReconnectLoop가 재연결 처리 */ }
    }

    private async Task HandleUeMessageAsync(string line)
    {
        try
        {
            using var doc  = JsonDocument.Parse(line);
            string    type = doc.RootElement.TryGetProperty("type", out var tp)
                             ? (tp.GetString() ?? "") : "";

            if (type != "ai_move_request") return;

            // UE5가 TCP로 보낸 AI 수 요청 처리
            string[] legalMoves  = GetStrArray(doc.RootElement, "legal_moves");
            string[] moveHistory = GetStrArray(doc.RootElement, "move_history");

            if (legalMoves.Length == 0) return;

            var req = new UEAiMoveRequest
            {
                Fen         = GetStr(doc.RootElement, "fen"),
                LegalMoves  = legalMoves,
                MoveHistory = moveHistory,
                AiLevel     = doc.RootElement.TryGetProperty("ai_level", out var al)
                              ? al.GetInt32() : 2,
                TimeLimitMs = doc.RootElement.TryGetProperty("time_limit_ms", out var tl)
                              ? tl.GetInt32() : 3000,
            };

            AiMoveResponse result = await _moveService.SelectMoveAsync(req);

            // 응답을 동일한 TCP 연결로 전송 (sendLock으로 직렬화됨)
            await SendCommandAsync(new
            {
                v         = 1,
                type      = "ai_move_response",
                ok        = result.Ok,
                moveUci   = result.MoveUci,
                commentKo = result.CommentKo,
                error     = result.Error ?? "",
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[HandleUeMessage] 오류: {ex.Message}");
        }
    }

    private static string GetStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string[] GetStrArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return arr.EnumerateArray()
                  .Select(x => x.GetString() ?? "")
                  .Where(s => s.Length > 0)
                  .ToArray();
    }

    // ── Ollama 상태 확인 루프 ────────────────────────────────────
    private void StartOllamaCheckLoop()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                bool alive = await _llm.IsAliveAsync();
                Dispatcher.Invoke(() => IsOllamaAlive = alive);
                await Task.Delay(10_000); // 10초마다 확인
            }
        });
    }

    // ── JSON 전송 ────────────────────────────────────────────────
    private async Task SendCommandAsync(object payload)
    {
        if (!IsSocketConnected())
        {
            ShowError("UE5에 연결되어 있지 않습니다. UE5 에디터/게임이 실행 중인지 확인하세요.");
            return;
        }

        try
        {
            await _sendLock.WaitAsync();
            try
            {
                string json = JsonSerializer.Serialize(payload) + "\n";
                byte[] data = Encoding.UTF8.GetBytes(json);
                await _stream!.WriteAsync(data);
                await _stream.FlushAsync();
            }
            finally { _sendLock.Release(); }

            Dispatcher.Invoke(() => ErrorText = string.Empty);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => IsConnected = false);
            ShowError($"전송 실패: {ex.Message}");
        }
    }

    private void ShowError(string msg) =>
        Dispatcher.Invoke(() => ErrorText = msg);

    // ── 버튼 핸들러 ──────────────────────────────────────────────
    private void OnRestart(object sender, RoutedEventArgs e)
    {
        _ = SendCommandAsync(new { v = 1, type = "command", cmd = "restart", requestId = NewId() });
    }

    private void OnVsAiWhite(object sender, RoutedEventArgs e)
    {
        if (!_isOllamaAlive)
        {
            ShowError("Ollama가 실행 중이지 않습니다. AI 대전을 시작할 수 없습니다.");
            return;
        }
        _ = SendCommandAsync(new
        {
            v = 1, type = "command", cmd = "new_game",
            options = new { playerColor = "WHITE", mode = "VS_AI", aiLevel = _aiLevel },
            requestId = NewId()
        });
    }

    private void OnVsAiBlack(object sender, RoutedEventArgs e)
    {
        if (!_isOllamaAlive)
        {
            ShowError("Ollama가 실행 중이지 않습니다. AI 대전을 시작할 수 없습니다.");
            return;
        }
        _ = SendCommandAsync(new
        {
            v = 1, type = "command", cmd = "new_game",
            options = new { playerColor = "BLACK", mode = "VS_AI", aiLevel = _aiLevel },
            requestId = NewId()
        });
    }

    private void OnAiLevelChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int level))
        {
            _aiLevel = level;
            Notify(nameof(AiLevel1));
            Notify(nameof(AiLevel2));
            Notify(nameof(AiLevel3));
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    protected override void OnClosed(EventArgs e)
    {
        _stream?.Dispose();
        _client?.Dispose();
        _llm.Dispose();
        base.OnClosed(e);
    }
}
