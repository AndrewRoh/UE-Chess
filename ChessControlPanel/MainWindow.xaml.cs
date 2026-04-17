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
    private int            _aiLevel      = 2;  // VS AI 단일 레벨
    private int            _aiLevelWhite = 2;  // AI VS AI 백 레벨
    private int            _aiLevelBlack = 2;  // AI VS AI 흑 레벨
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // ── 상태 ────────────────────────────────────────────────────
    private bool   _isUeConnected;
    private bool   _isOllamaAlive;
    private string _errorText = string.Empty;

    // ── AI VS AI 자동 대전 ───────────────────────────────────────
    private bool                   _isAutoPlay;
    private CancellationTokenSource? _autoPlayCts;

    // ── 마지막 AI 수 정보 ────────────────────────────────────────
    private bool   _hasLastMove;
    private string _lastMoveUci     = string.Empty;
    private string _lastLevelText   = string.Empty;
    private string _lastLatencyText = string.Empty;
    private bool   _lastIsFallback;
    private string _lastComment     = string.Empty;

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

    public string OllamaHostText  => _llm.OllamaHost;
    public string OllamaModelText => _llm.Model;

    public string ErrorText
    {
        get => _errorText;
        set { _errorText = value; Notify(nameof(ErrorText)); Notify(nameof(ErrorVisibility)); }
    }

    public Visibility ErrorVisibility =>
        string.IsNullOrEmpty(_errorText) ? Visibility.Collapsed : Visibility.Visible;

    // VS AI 레벨 바인딩
    public bool AiLevel1 => _aiLevel == 1;
    public bool AiLevel2 => _aiLevel == 2;
    public bool AiLevel3 => _aiLevel == 3;

    // AI VS AI 백/흑 레벨 바인딩
    public bool AutoWhiteLevel1 => _aiLevelWhite == 1;
    public bool AutoWhiteLevel2 => _aiLevelWhite == 2;
    public bool AutoWhiteLevel3 => _aiLevelWhite == 3;
    public bool AutoBlackLevel1 => _aiLevelBlack == 1;
    public bool AutoBlackLevel2 => _aiLevelBlack == 2;
    public bool AutoBlackLevel3 => _aiLevelBlack == 3;

    public bool IsAutoPlay
    {
        get => _isAutoPlay;
        set
        {
            _isAutoPlay = value;
            Notify(nameof(IsAutoPlay));
            Notify(nameof(AutoPlayButtonText));
            Notify(nameof(AutoPlayButtonColor));
            Notify(nameof(AutoPlayStatusText));
        }
    }

    public string AutoPlayButtonText =>
        _isAutoPlay ? "⏹  Auto Play 중지" : "▶  AI VS AI — Auto Play";

    public System.Windows.Media.Brush AutoPlayButtonColor =>
        _isAutoPlay
            ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0xDD, 0xDD))
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xDD, 0xFF, 0xDD));

    private string _autoPlayStatusText = string.Empty;
    public string AutoPlayStatusText
    {
        get => _autoPlayStatusText;
        set { _autoPlayStatusText = value; Notify(nameof(AutoPlayStatusText)); }
    }

    // ── 마지막 AI 수 속성 ────────────────────────────────────────
    public bool HasLastMove
    {
        get => _hasLastMove;
        set { _hasLastMove = value; Notify(nameof(HasLastMove)); Notify(nameof(LastMoveVisibility)); }
    }
    public string LastMoveUci
    {
        get => _lastMoveUci;
        set { _lastMoveUci = value; Notify(nameof(LastMoveUci)); }
    }
    public string LastLevelText
    {
        get => _lastLevelText;
        set { _lastLevelText = value; Notify(nameof(LastLevelText)); }
    }
    public string LastLatencyText
    {
        get => _lastLatencyText;
        set { _lastLatencyText = value; Notify(nameof(LastLatencyText)); }
    }
    public bool LastIsFallback
    {
        get => _lastIsFallback;
        set { _lastIsFallback = value; Notify(nameof(LastIsFallback)); Notify(nameof(LastFallbackText)); Notify(nameof(LastFallbackColor)); }
    }
    public string LastComment
    {
        get => _lastComment;
        set { _lastComment = value; Notify(nameof(LastComment)); Notify(nameof(LastCommentVisibility)); }
    }

    public Visibility LastMoveVisibility =>
        _hasLastMove ? Visibility.Visible : Visibility.Collapsed;

    public string LastFallbackText =>
        _lastIsFallback ? "예 (LLM 실패)" : "아니요";

    public System.Windows.Media.Brush LastFallbackColor =>
        _lastIsFallback
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red)
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0x88, 0x22));

    public Visibility LastCommentVisibility =>
        string.IsNullOrEmpty(_lastComment) ? Visibility.Collapsed : Visibility.Visible;

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

            if (type == "game_over")
            {
                string winner = doc.RootElement.TryGetProperty("winner", out var w)
                                ? (w.GetString() ?? "") : "?";
                Dispatcher.Invoke(() =>
                    AutoPlayStatusText = $"게임 종료 — 승자: {winner}");

                if (_isAutoPlay)
                    _ = Task.Run(() => AutoPlayRestartDelayAsync());
                return;
            }

            if (type != "ai_move_request") return;

            // UE5가 TCP로 보낸 AI 수 요청 처리
            string[] legalMoves  = GetStrArray(doc.RootElement, "legal_moves");
            string[] moveHistory = GetStrArray(doc.RootElement, "move_history");

            if (legalMoves.Length == 0) return;

            // 차례(side) 결정: 메시지 명시 > FEN 파싱
            string side = GetStr(doc.RootElement, "side_to_move");
            if (string.IsNullOrEmpty(side))
            {
                string fen = GetStr(doc.RootElement, "fen");
                var fenParts = fen.Split(' ');
                side = fenParts.Length >= 2 ? fenParts[1] : "w";
            }

            // AI VS AI 모드면 흑/백 각 레벨, VS AI 모드면 단일 레벨
            int resolvedLevel = _isAutoPlay
                ? (side.StartsWith("b") ? _aiLevelBlack : _aiLevelWhite)
                : (doc.RootElement.TryGetProperty("ai_level", out var al) ? al.GetInt32() : _aiLevel);

            bool inCheck = doc.RootElement.TryGetProperty("in_check", out var ic)
                           && ic.ValueKind == JsonValueKind.True;

            var req = new UEAiMoveRequest
            {
                Fen         = GetStr(doc.RootElement, "fen"),
                LegalMoves  = legalMoves,
                MoveHistory = moveHistory,
                Level       = resolvedLevel,
                SideToMove  = side,
                TimeLimitMs = doc.RootElement.TryGetProperty("time_limit_ms", out var tl)
                              ? tl.GetInt32() : 3000,
                InCheck     = inCheck,
            };

            AiMoveResponse result = await _moveService.SelectMoveAsync(req);

            // 마지막 AI 수 디버그 패널 업데이트
            string levelName = result.LevelUsed switch { 1 => "초보", 2 => "중급", 3 => "고급", _ => "?" };
            Dispatcher.Invoke(() =>
            {
                HasLastMove     = true;
                LastMoveUci     = result.MoveUci ?? "-";
                LastLevelText   = $"{levelName} (Lv.{result.LevelUsed ?? _aiLevel})";
                LastLatencyText = result.LatencyMs.HasValue ? $"{result.LatencyMs} ms" : "-";
                LastIsFallback  = result.IsFallback;
                LastComment     = result.CommentKo ?? string.Empty;
            });

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

    private void OnToggleAutoPlay(object sender, RoutedEventArgs e)
    {
        if (_isAutoPlay)
        {
            // 중지 — CTS 취소로 게임 종료 후 자동 재시작만 막음 (현재 게임은 자연 종료)
            _autoPlayCts?.Cancel();
            _autoPlayCts = null;
            IsAutoPlay = false;
            AutoPlayStatusText = string.Empty;
        }
        else
        {
            if (!_isOllamaAlive)
            {
                ShowError("Ollama가 실행 중이지 않습니다. AI VS AI를 시작할 수 없습니다.");
                return;
            }
            _autoPlayCts = new CancellationTokenSource();
            Dispatcher.Invoke(() =>
            {
                IsAutoPlay = true;
                AutoPlayStatusText = "AI VS AI 진행 중...";
            });
            SendAiVsAiNewGame();
        }
    }

    private void SendAiVsAiNewGame()
    {
        _ = SendCommandAsync(new
        {
            v = 1, type = "command", cmd = "new_game",
            options = new { playerColor = "WHITE", mode = "AI_VS_AI", aiLevel = _aiLevel },
            requestId = NewId()
        });
        Dispatcher.Invoke(() => AutoPlayStatusText = "AI VS AI 진행 중...");
    }

    private async Task AutoPlayRestartDelayAsync()
    {
        var cts = _autoPlayCts;
        if (cts == null) return;

        try
        {
            // 7초 대기 후 자동 재시작 (매초 카운트다운 표시)
            for (int i = 7; i > 0; i--)
            {
                if (cts.Token.IsCancellationRequested) return;
                int remaining = i;
                Dispatcher.Invoke(() =>
                    AutoPlayStatusText = $"다음 게임 시작까지 {remaining}초...");
                await Task.Delay(1000, cts.Token);
            }

            if (!cts.Token.IsCancellationRequested && _isAutoPlay && _autoPlayCts == cts)
                Dispatcher.Invoke(SendAiVsAiNewGame);
        }
        catch (OperationCanceledException) { }
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

    private void OnAutoAiWhiteLevelChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int level))
        {
            _aiLevelWhite = level;
            Notify(nameof(AutoWhiteLevel1));
            Notify(nameof(AutoWhiteLevel2));
            Notify(nameof(AutoWhiteLevel3));
        }
    }

    private void OnAutoAiBlackLevelChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int level))
        {
            _aiLevelBlack = level;
            Notify(nameof(AutoBlackLevel1));
            Notify(nameof(AutoBlackLevel2));
            Notify(nameof(AutoBlackLevel3));
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    protected override void OnClosed(EventArgs e)
    {
        _autoPlayCts?.Cancel();
        _autoPlayCts?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _llm.Dispose();
        base.OnClosed(e);
    }
}
