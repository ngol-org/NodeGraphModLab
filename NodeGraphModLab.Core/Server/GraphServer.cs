using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Reflection;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.Core.Extensions;
using NodeGraphModLab.Server.Handlers;

namespace NodeGraphModLab.Server;

/// <summary>
/// 内蔵 WebSocket + HTTP サーバー。
/// - http://127.0.0.1:{port}/         → WebUI 静的ファイル配信
/// - ws://127.0.0.1:{port}/ws         → WebSocket エンドポイント
/// </summary>
public sealed class GraphServer : IDisposable
{
    private readonly int _port;
    private readonly NodeRegistry _registry;
    private readonly GraphExecutor _executor;
    private readonly INgolLogger _log;
    private readonly string _graphSaveDir;
    private readonly string _webUiDir;
    private readonly string _dynamicNodesDir;
    private readonly string _pluginDir;
    private readonly PersistentNodeRunner _runner;
    private readonly ILiveParamStore _liveParamStore;
    private readonly IDebugLogStore _debugLogStore;
    private readonly IKVStore? _store;
    private readonly string _pluginVersion;
    private readonly string _gameName;
    private readonly string _runtimeType;
    private readonly ExtensionServiceRegistry? _extensionServices;
    private readonly ExtensionHost? _extensionHost;

#if NET6_0_OR_GREATER
    private HttpListener? _listener;
#else
    private System.Net.Sockets.TcpListener? _tcpListener;
#endif
    private Thread? _listenerThread;
    private readonly ConcurrentBag<WebSocketSession> _sessions = new();
    private readonly List<WebSocketSession> _browserSessions = new();
    private readonly object _browserSessionsLock = new();
    private bool _disposed;

    // グラフ実行リクエストをメインスレッドキューに積む
    private static readonly ConcurrentQueue<PendingExecution> _pendingExecutions = new();

    // 現在実行中のグラフをキャンセルするためのCTS
    private CancellationTokenSource? _executionCts;

    private readonly Dictionary<string, IMessageHandler> _handlers;

    public bool IsRunning { get; private set; }

    public GraphServer(
        int port,
        NodeRegistry registry,
        GraphExecutor executor,
        INgolLogger log,
        string graphSaveDir,
        string webUiDir,
        PersistentNodeRunner runner,
        string? dynamicNodesDir = null,
        string? nodesDir = null,
        string? nodePacksDir = null,
        ConcurrentDictionary<string, string>? scriptNodeId = null,
        IKVStore? store = null,
        string pluginVersion = "",
        string gameName = "",
        ExtensionServiceRegistry? extensionServices = null,
        ExtensionHost? extensionHost = null,
        string? runtimeType = null)
    {
        _port = port;
        _registry = registry;
        _executor = executor;
        _log = log;
        _graphSaveDir = graphSaveDir;
        _webUiDir = webUiDir;
        _runner = runner;
        _liveParamStore = new LiveParamStore();
        _debugLogStore = new DebugLogStore();
        _store = store;
        _pluginVersion = pluginVersion;
        _gameName = gameName;
        // ホストが RuntimeType を明示指定しない場合は既定値を使用。
        _runtimeType = runtimeType ?? "Unknown";
        _extensionServices = extensionServices;
        _extensionHost = extensionHost;
        _dynamicNodesDir = dynamicNodesDir ?? Path.Combine(graphSaveDir, "..", "dynamic-nodes");
        _pluginDir = Path.GetFullPath(Path.Combine(graphSaveDir, ".."));

        var resolvedNodesDir = nodesDir ?? Path.Combine(graphSaveDir, "..", "Nodes", "CustomNodes", "cs");
        var resolvedNodePacksDir = nodePacksDir ?? Path.Combine(graphSaveDir, "..", "Nodes", "CustomNodes", "dll");
        var resolvedScriptNodeId = scriptNodeId ?? new ConcurrentDictionary<string, string>();

        Directory.CreateDirectory(_graphSaveDir);

        var ctx = new HandlerContext(
            registry, _liveParamStore, _debugLogStore, log, _graphSaveDir, _dynamicNodesDir,
            resolvedNodesDir, resolvedNodePacksDir, resolvedScriptNodeId,
            _pendingExecutions, runner,
            () => _executionCts,
            executor,
            extensionServices,
            _store,
            SendOpenGraphToLatestBrowser);

        IMessageHandler[] handlerList =
        [
            new GetNodeListHandler(ctx),
            new ExecuteGraphHandler(ctx),
            new ExecuteFragmentHandler(ctx),
            new ExecuteAllFragmentsHandler(ctx),
            new StopGraphHandler(ctx),
            new SetSnapshotPinHandler(),
            new GetSnapshotHistoryHandler(ctx),
            new RestoreSnapshotHandler(),
            new ClearSnapshotHandler(),
            new ClearAllSnapshotsHandler(),
            new GetSnapshotStoreStateHandler(),
            new StopPersistentHandler(ctx),
            new StopPersistentNodeHandler(ctx),
            new ListPersistentNodesHandler(ctx),
            new SaveGraphHandler(ctx),
            new LoadGraphHandler(ctx),
            new OpenGraphHandler(ctx),
            new ListGraphsHandler(ctx),
            new DeleteGraphHandler(ctx),
            new CompileNodeHandler(ctx),
            new ExportNodesHandler(ctx),
            new OpenNodeFolderHandler(ctx),
            new ExecuteNodeHandler(ctx),
            new ReleaseSnapshotHandler(),
            new SetSnapshotValueHandler(),
            new PushNodeLiveParamsHandler(ctx),
            new DebugLogEntryHandler(ctx),
            new GetDebugLogHandler(ctx),
        ];
        _handlers = handlerList.ToDictionary(h => h.MessageType);

        _runner.OnChanged = nodes =>
        {
            var push = new PersistentNodeChangedPush
            {
                ActiveNodes = nodes.Select(m => new PersistentNodeInfo
                {
                    NodeInstanceId = m.NodeInstanceId,
                    DisplayName = m.DisplayName,
                    GraphName = m.GraphName,
                }).ToList()
            };
            _ = BroadcastAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.PersistentNodeChangedPush));
        };
    }

    public void Start()
    {
        if (IsRunning) return;

#if NET6_0_OR_GREATER
        // HttpListener: ループバック（127.0.0.1 と localhost）のみ受け付ける
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            _log.LogError($"[GraphServer] Failed to start listener on port {_port}: {ex.Message}");
            return;
        }
        IsRunning = true;
        _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "GraphServer.Listener" };
        _listenerThread.Start();
#else
        // Mono: HttpListener は WebSocket 非対応なので TcpListener を使用
        try
        {
            _tcpListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, _port);
            _tcpListener.Start();
        }
        catch (Exception ex)
        {
            _log.LogError($"[GraphServer] Failed to start TcpListener on port {_port}: {ex.Message}");
            return;
        }
        IsRunning = true;
        _listenerThread = new Thread(TcpListenLoop) { IsBackground = true, Name = "GraphServer.TcpListener" };
        _listenerThread.Start();
#endif

        _log.LogInfo($"[Node Graph Mod Lab] Graph Editor: http://localhost:{_port}");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
#if NET6_0_OR_GREATER
        _listener?.Stop();
#else
        _tcpListener?.Stop();
#endif
    }

    /// <summary>ホストの更新ループ（NgolRuntime.Tick() 経由）から呼び出す。保留中のグラフ実行を処理する。</summary>
    public void DrainPendingExecutions()
    {
        while (_pendingExecutions.TryDequeue(out var pending))
        {
            var session = pending.Session;
            var graph = pending.Graph;

            // 既存の実行をキャンセル
            _executionCts?.Cancel();
            _executionCts?.Dispose();
            _executionCts = new CancellationTokenSource();
            var cts = _executionCts;

            Task.Run(async () =>
            {
                var baseCtx = new MainThreadExecutionContext("engine", _log, _runner, _registry, _store, _liveParamStore, _extensionServices);

                // Snapshot 保存通知を収集（ピン留めノードはスキップ）
                var savedSnapshots = new System.Collections.Concurrent.ConcurrentBag<(string nodeId, string portName, string valueType, string? valueString)>();
                if (session.SnapshotStore is NotifyingSnapshotStore notifyingStore)
                {
                    notifyingStore.OnSet = (nodeId, portName, value) =>
                    {
                        _log.LogInfo($"[OnSet] nodeId={nodeId} port={portName} val={value}");
                        var typeName = value?.GetType().Name ?? "null";
                        var valueString = value?.ToString();
                        savedSnapshots.Add((nodeId, portName, typeName, valueString));
                    };
                    // CanSet: PIN 状態を常に最新の PinnedSnapshotNodeIds で判定（LivePush 時も有効）
                    notifyingStore.CanSet = nodeId => !session.PinnedSnapshotNodeIds.Contains(nodeId);
                }

                // ノード実行進捗を全セッションにブロードキャスト
                _executor.OnNodeProgress = async (nodeId, status, durationMs) =>
                {
                    var progress = new ExecutionProgressPush
                    {
                        NodeInstanceId = nodeId,
                        Status = status,
                        DurationMs = durationMs
                    };
                    await BroadcastAsync(JsonSerializer.Serialize(progress, ServerJsonContext.Default.ExecutionProgressPush));
                };

                ExecutionResult result;
                string? fragmentId = null;

                if (pending.FragmentId != null)
                {
                    // 断片実行
                    fragmentId = pending.FragmentId;
                    result = _executor.ExecuteFragment(graph, fragmentId, baseCtx,
                        session.SnapshotStore, pending.PinnedFragmentIds, cts.Token);
                }
                else if (pending.ExecuteAll)
                {
                    // 全断片実行
                    result = _executor.ExecuteAllFragments(graph, baseCtx,
                        session.SnapshotStore, pending.PinnedFragmentIds, cts.Token);
                }
                else
                {
                    // fragmentLinks があれば断片順実行に自動切替（snapshotStore が必要なため）
                    result = graph.FragmentLinks.Count > 0
                        ? _executor.ExecuteAllFragments(graph, baseCtx, session.SnapshotStore, null, cts.Token)
                        : _executor.Execute(graph, baseCtx, cts.Token, session.SnapshotStore);
                }

                _executor.OnNodeProgress = null;
                if (session.SnapshotStore is NotifyingSnapshotStore ns2)
                {
                    ns2.OnSet = null;
                    // CanSet はリセットしない（LivePush でも常に PIN チェックが働くようにする）
                }

                // Snapshot 保存通知を Push
                foreach (var (savedNodeId, portName, valueType, valueString) in savedSnapshots)
                {
                    var push = new SnapshotSavedPush
                    {
                        NodeInstanceId = savedNodeId,
                        PortName = portName,
                        ValueType = valueType,
                        ValueString = valueString,
                    };
                    await session.SendAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.SnapshotSavedPush));
                }

                // ログを全セッションに Push
                foreach (var entry in result.Logs)
                {
                    var push = new ExecutionLogPush
                    {
                        NodeInstanceId = entry.NodeInstanceId,
                        Message = entry.Message,
                        Level = entry.Level.ToString().ToLower(),
                        TimestampMs = entry.Timestamp.ToUnixTimeMilliseconds()
                    };
                    await BroadcastAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.ExecutionLogPush));
                }

                var response = new ExecutionResultResponse
                {
                    Success = result.Success,
                    ErrorMessage = result.ErrorMessage,
                    DurationMs = result.Duration.TotalMilliseconds,
                    FragmentId = fragmentId
                };
                await session.SendAsync(JsonSerializer.Serialize(response, ServerJsonContext.Default.ExecutionResultResponse));
            });
        }

        // メインスレッドキューのアクション（INode.MainThreadDispatch 経由のもの）を処理
        MainThreadExecutionContext.DrainMainThreadQueue();

        // LivePush 通知をドレインしてブロードキャスト（PushLiveValue 経由のリアルタイム更新）
        foreach (var session in _sessions)
        {
            if (session.SnapshotStore is not NotifyingSnapshotStore liveStore) continue;
            while (liveStore.LiveNotifications.TryDequeue(out var notif))
            {
                var push = new SnapshotSavedPush
                {
                    NodeInstanceId = notif.nodeId,
                    PortName = notif.portName,
                    ValueType = notif.value?.GetType().Name ?? "null",
                    ValueString = notif.value?.ToString(),
                };
                var json = JsonSerializer.Serialize(push, ServerJsonContext.Default.SnapshotSavedPush);
                _ = BroadcastAsync(json);
            }
        }
    }

    /// <summary>起動時自動実行用。ダミーセッションでグラフを実行キューに積む。</summary>
    public void EnqueueStartupExecution(NodeGraph graph)
    {
        _pendingExecutions.Enqueue(new PendingExecution(StartupNullSession.Instance, graph));
    }

    /// <summary>起動時自動実行用。単一ノードを直接実行し、結果はログのみに出力する。</summary>
    public void RunStartupNode(string nodeTypeId, JsonElement inputs)
    {
        try
        {
            var (paramValues, inputValues) = ResolveStartupInputs(inputs);
            var baseCtx = new MainThreadExecutionContext(
                "engine", _log, _runner, _registry, _store, _liveParamStore, _extensionServices);

            var result = _executor.ExecuteSingleNode(nodeTypeId, paramValues, inputValues, baseCtx);

            if (result.Success)
                _log.LogInfo($"[Startup] Node '{nodeTypeId}' completed in {result.Duration.TotalMilliseconds:F0}ms");
            else
                _log.LogError($"[Startup] Node '{nodeTypeId}' failed: {result.ErrorMessage}");

            foreach (var entry in result.Logs)
            {
                var prefix = $"[Startup] [{entry.NodeInstanceId}] {entry.Message}";
                switch (entry.Level)
                {
                    case LogLevel.Error: _log.LogError(prefix); break;
                    case LogLevel.Warning: _log.LogWarning(prefix); break;
                    case LogLevel.Debug: _log.LogDebug(prefix); break;
                    default: _log.LogInfo(prefix); break;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"[Startup] RunStartupNode error: {ex.Message}");
        }
    }

    private static (Dictionary<string, JsonElement> paramValues, Dictionary<string, object?> inputValues)
        ResolveStartupInputs(JsonElement inputs)
    {
        var paramValues = new Dictionary<string, JsonElement>();
        var inputValues = new Dictionary<string, object?>();

        if (inputs.ValueKind != JsonValueKind.Object)
            return (paramValues, inputValues);

        foreach (var prop in inputs.EnumerateObject())
        {
            var portName = prop.Name;
            var val = prop.Value;
            paramValues[portName] = val;
            inputValues[portName] = val.ValueKind switch
            {
                JsonValueKind.Number => val.GetDouble(),
                JsonValueKind.String => val.GetString(),
                JsonValueKind.True => (object?)true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => val.GetRawText()
            };
        }

        return (paramValues, inputValues);
    }

    // ---- HTTP + WebSocket ループ ----

#if NET6_0_OR_GREATER
    private void ListenLoop()
    {
        while (IsRunning && _listener != null)
        {
            try
            {
                var context = _listener.GetContext();
                _ = Task.Run(() => HandleContextAsync(context));
            }
            catch (HttpListenerException) { /* サーバー停止時は正常 */ }
            catch (Exception ex)
            {
                _log.LogError($"[GraphServer] ListenLoop error: {ex.Message}");
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            var url = context.Request.Url?.LocalPath ?? "/";
            var isWs = context.Request.IsWebSocketRequest;
            _log.LogDebug($"[GraphServer] Request: {url} IsWS={isWs} Method={context.Request.HttpMethod}");
            if (isWs)
            {
                var presentedToken = context.Request.Headers["Sec-WebSocket-Protocol"];
                if (!ConnectionAuthToken.Validate(presentedToken))
                {
                    _log.LogWarning("[GraphServer] WS handshake rejected: invalid or missing auth token");
                    context.Response.StatusCode = 401;
                    context.Response.Close();
                    return;
                }
                // 認証の要否に関わらず、クライアントが要求したサブプロトコルは常にそのままエコーする。
                // 値の妥当性は上の Validate() で既に検証済み。認証無効時だからと null を返すと、
                // クライアントが要求したのにサーバーが応答しない状態になり、RFC 6455 違反として
                // Chrome が接続を強制切断する（Edge/Firefox は再現しない）。
                var subProtocol = !string.IsNullOrEmpty(presentedToken) ? presentedToken : null;
                var wsContext = await context.AcceptWebSocketAsync(subProtocol);
                var session = new WebSocketSession(wsContext.WebSocket);
                var query = context.Request.Url?.Query ?? "";
                if (query.Contains("client=mcp"))
                    session.IsToolClient = true;
                _sessions.Add(session);
                if (!session.IsToolClient)
                {
                    lock (_browserSessionsLock) { _browserSessions.Add(session); }
                }
                _log.LogDebug($"[GraphServer] WebSocket accepted, starting handler");
                await HandleWebSocketAsync(session);
            }
            else
            {
                await ServeStaticFileAsync(context);
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"[GraphServer] HandleContext error: {ex.GetType().Name}: {ex.Message}");
        }
    }

#else
    // ---- Mono 用 TcpListener ベースのサーバー ----

    private void TcpListenLoop()
    {
        while (IsRunning && _tcpListener != null)
        {
            try
            {
                var client = _tcpListener.AcceptTcpClient();
                _ = Task.Run(() => HandleTcpClientAsync(client));
            }
            catch { /* サーバー停止時は正常 */ }
        }
    }

    private async Task HandleTcpClientAsync(System.Net.Sockets.TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var req = await RawHttpParser.ParseAsync(stream);
                if (req == null) return;

                _log.LogDebug($"[GraphServer] TCP Request: {req.Path} IsWS={req.IsWebSocketUpgrade}");

                if (req.IsWebSocketUpgrade)
                {
                    var presentedToken = req.Headers.TryGetValue("sec-websocket-protocol", out var spHeader) ? spHeader : null;
                    if (!ConnectionAuthToken.Validate(presentedToken))
                    {
                        _log.LogWarning("[GraphServer] WS handshake rejected: invalid or missing auth token");
                        await WriteTcpResponseAsync(stream, 401, "Unauthorized", "text/plain", Encoding.UTF8.GetBytes("Unauthorized"));
                        return;
                    }
                    // 認証の要否に関わらず、クライアントが要求したサブプロトコルは常にそのままエコーする。
                    // 理由は AcceptWebSocketAsync 側のコメント参照（Chrome の RFC 6455 違反判定回避）。
                    var subProtocol = !string.IsNullOrEmpty(presentedToken) ? presentedToken : null;
                    var ws = await MonoWebSocketHelper.AcceptAsync(client, req, subProtocol);
                    if (ws == null) return;
                    var session = new WebSocketSession(ws);
                    if (req.Path.Contains("client=mcp"))
                        session.IsToolClient = true;
                    _sessions.Add(session);
                    if (!session.IsToolClient)
                    {
                        lock (_browserSessionsLock) { _browserSessions.Add(session); }
                    }
                    _log.LogDebug("[GraphServer] TCP WebSocket accepted");
                    await HandleWebSocketAsync(session);
                }
                else
                {
                    await ServeStaticFileTcpAsync(stream, req);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"[GraphServer] TcpClient error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task ServeStaticFileTcpAsync(System.IO.Stream stream, RawHttpRequest req)
    {
        var urlPath = req.Path;
        // クエリ文字列を除去
        int qmark = urlPath.IndexOf('?');
        if (qmark >= 0) urlPath = urlPath.Substring(0, qmark);

        // WebUI 外部プラグインのマニフェスト API
        if (urlPath == WebUiPluginManifest.EndpointPath)
        {
            var manifestBytes = Encoding.UTF8.GetBytes(WebUiPluginManifest.BuildJson(_webUiDir));
            await WriteTcpResponseAsync(stream, 200, "OK", "application/json", manifestBytes);
            return;
        }

        if (urlPath == ExtensionManifestApi.EndpointPath)
        {
            var manifestBytes = Encoding.UTF8.GetBytes(ExtensionManifestApi.BuildJson(_extensionHost));
            await WriteTcpResponseAsync(stream, 200, "OK", "application/json", manifestBytes);
            return;
        }

        if (urlPath == "/" || urlPath.Length == 0) urlPath = "/index.html";

        var safePath = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        if (safePath.Contains(".."))
        {
            await WriteTcpResponseAsync(stream, 400, "Bad Request", "text/plain", Encoding.UTF8.GetBytes("Bad Request"));
            return;
        }

        var filePath = Path.Combine(_webUiDir, safePath);
        if (!File.Exists(filePath))
            filePath = Path.Combine(_webUiDir, "index.html");

        if (!File.Exists(filePath))
        {
            await WriteTcpResponseAsync(stream, 404, "Not Found", "text/plain",
                Encoding.UTF8.GetBytes("WebUI not found. Build the WebUI first."));
            return;
        }

        var bytes = File.ReadAllBytes(filePath);
        var contentType = GetContentType(filePath);
        await WriteTcpResponseAsync(stream, 200, "OK", contentType, bytes);
    }

    private static async Task WriteTcpResponseAsync(System.IO.Stream stream, int status, string statusText,
        string contentType, byte[] body)
    {
        var header = $"HTTP/1.1 {status} {statusText}\r\n"
            + $"Content-Type: {contentType}\r\n"
            + $"Content-Length: {body.Length}\r\n"
            + "Connection: close\r\n"
            + "\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
        await stream.WriteAsync(body, 0, body.Length);
    }
#endif


    private async Task HandleWebSocketAsync(WebSocketSession session)
    {
        _log.LogDebug($"[GraphServer] WS handler start, state={session.WebSocket.State}");
        try
        {
        // 接続直後に welcome メッセージを送信
        var welcome = new WelcomeMessage
        {
            PluginVersion = _pluginVersion,
            PluginDir = _pluginDir,
            GameName = _gameName,
            RuntimeType = _runtimeType,
        };
        await session.SendAsync(JsonSerializer.Serialize(welcome, ServerJsonContext.Default.WelcomeMessage));
        _log.LogDebug("[GraphServer] WS welcome sent");

        // 現在の永続ノードリストを新規接続クライアントへ送信（ページ再読み込み後の状態復元）
        var persistentPush = new PersistentNodeChangedPush
        {
            ActiveNodes = _runner.GetActiveNodes().Select(m => new PersistentNodeInfo
            {
                NodeInstanceId = m.NodeInstanceId,
                DisplayName = m.DisplayName,
                GraphName = m.GraphName,
            }).ToList()
        };
        await session.SendAsync(JsonSerializer.Serialize(persistentPush, ServerJsonContext.Default.PersistentNodeChangedPush));

        var buffer = new byte[64 * 1024];
        while (session.WebSocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await session.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await session.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await DispatchMessageAsync(session, json);
                }
            }
            catch (WebSocketException ex) { _log.LogDebug($"[GraphServer] WS recv exception: {ex.Message}"); break; }
            catch (Exception ex)
            {
                _log.LogError($"[GraphServer] WebSocket error: {ex.Message}");
                break;
            }
        }
        _log.LogDebug($"[GraphServer] WS handler end, state={session.WebSocket.State}");
        }
        catch (Exception ex)
        {
            _log.LogError($"[GraphServer] WS handler exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task DispatchMessageAsync(ISession session, string json)
    {
        var (msgType, doc) = MessageParser.ParseType(json);
        if (msgType == null || doc == null)
        {
            await session.SendAsync(JsonSerializer.Serialize(
                new ErrorResponse { Message = "Invalid message format" },
                ServerJsonContext.Default.ErrorResponse));
            return;
        }

        try
        {
            if (_handlers.TryGetValue(msgType, out var handler))
                await handler.HandleAsync(session, doc.RootElement);
            else
                await session.SendAsync(JsonSerializer.Serialize(
                    new ErrorResponse { Message = $"Unknown message type: {msgType}" },
                    ServerJsonContext.Default.ErrorResponse));
        }
        catch (Exception ex)
        {
            _log.LogError($"[GraphServer] Dispatch error ({msgType}): {ex.Message}");
            // WebSocket 切断が原因の場合はエラー送信を試みない（二重例外を防ぐ）
            if (ex is not WebSocketException)
                await session.SendAsync(JsonSerializer.Serialize(
                    new ErrorResponse { Message = ex.Message },
                    ServerJsonContext.Default.ErrorResponse));
        }
    }

    // ---- 静的ファイル配信 ----

#if NET6_0_OR_GREATER
    private async Task ServeStaticFileAsync(HttpListenerContext context)
    {
        var urlPath = context.Request.Url?.LocalPath ?? "/";

        // WebUI 外部プラグインのマニフェスト API
        if (urlPath == WebUiPluginManifest.EndpointPath)
        {
            var manifestBytes = Encoding.UTF8.GetBytes(WebUiPluginManifest.BuildJson(_webUiDir));
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = manifestBytes.Length;
            await context.Response.OutputStream.WriteAsync(manifestBytes, 0, manifestBytes.Length);
            context.Response.Close();
            return;
        }

        if (urlPath == ExtensionManifestApi.EndpointPath)
        {
            var extManifestBytes = Encoding.UTF8.GetBytes(ExtensionManifestApi.BuildJson(_extensionHost));
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = extManifestBytes.Length;
            await context.Response.OutputStream.WriteAsync(extManifestBytes, 0, extManifestBytes.Length);
            context.Response.Close();
            return;
        }

        if (urlPath == "/") urlPath = "/index.html";

        // パストラバーサル対策
        var safePath = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        if (safePath.Contains(".."))
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        var filePath = Path.Combine(_webUiDir, safePath);
        if (!File.Exists(filePath))
        {
            // SPA フォールバック: index.html を返す
            filePath = Path.Combine(_webUiDir, "index.html");
        }

        if (!File.Exists(filePath))
        {
            context.Response.StatusCode = 404;
            var msg = Encoding.UTF8.GetBytes("WebUI not found. Build the WebUI first.");
            context.Response.ContentLength64 = msg.Length;
            await context.Response.OutputStream.WriteAsync(msg, 0, msg.Length);
            context.Response.Close();
            return;
        }

        var contentType = GetContentType(filePath);
        context.Response.ContentType = contentType;
        context.Response.StatusCode = 200;

        var bytes = await File.ReadAllBytesAsync(filePath);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        context.Response.Close();
    }
#endif
    private static string GetContentType(string filePath) => Path.GetExtension(filePath).ToLower() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "application/javascript",
        ".css" => "text/css",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        _ => "application/octet-stream"
    };

    // ---- ブロードキャスト ----

    /// <summary>
    /// scripts/ ホットリロード完了後に全セッションへ通知する。
    /// nodeId が null の場合は全登録ノード ID を送信する（レジストリ再構築後の全更新用）。
    /// </summary>
    public void BroadcastNodeListUpdated(string? nodeId)
    {
        var ids = nodeId != null
            ? new List<string> { nodeId }
            : _registry.GetAll().Select(d => d.Id).ToList();
        var push = new NodeListUpdatedPush { UpdatedNodeTypeIds = ids };
        _ = BroadcastAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.NodeListUpdatedPush));
    }

    /// <summary>スクリプトコンパイル失敗を全セッションへブロードキャストする。</summary>
    public void BroadcastScriptCompileError(string fileName, string errorMessage, List<string>? diagnostics = null)
    {
        var push = new ScriptCompileErrorPush { FileName = fileName, ErrorMessage = errorMessage, Diagnostics = diagnostics };
        _ = BroadcastAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.ScriptCompileErrorPush));
    }

    /// <summary>警告ログを全セッションの Execution Log パネルへブロードキャストする。</summary>
    public void BroadcastWarningLog(string message)
    {
        var push = new ExecutionLogPush
        {
            Message = message,
            Level = "warning",
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        _ = BroadcastAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.ExecutionLogPush));
    }

    public async Task<bool> SendOpenGraphToLatestBrowser(string graphId)
    {
        WebSocketSession? target = null;
        lock (_browserSessionsLock)
        {
            for (int i = _browserSessions.Count - 1; i >= 0; i--)
            {
                if (_browserSessions[i].WebSocket.State != WebSocketState.Open)
                {
                    _browserSessions.RemoveAt(i);
                    continue;
                }
                target = _browserSessions[i];
                break;
            }
        }
        if (target == null) return false;
        await target.SendAsync(JsonSerializer.Serialize(
            new OpenGraphPush { GraphId = graphId }, ServerJsonContext.Default.OpenGraphPush));
        return true;
    }

    private async Task BroadcastAsync(string message)
    {
        foreach (var session in _sessions)
        {
            try { await session.SendAsync(message); }
            catch { /* 切断済みセッションはスキップ */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
#if NET6_0_OR_GREATER
        _listener?.Close();
#else
        _tcpListener?.Stop();
#endif
    }
}

// ---- WebSocket セッション ----

public sealed class WebSocketSession : ISession
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    public WebSocket WebSocket { get; }
    public bool IsToolClient { get; set; }

    /// <summary>セッション単位のスナップショットストア（ホストプロセス起動中インメモリ保持）。</summary>
    public NotifyingSnapshotStore SnapshotStore { get; } = new NotifyingSnapshotStore();

    /// <summary>ピン留めされた Snapshot ノードインスタンス ID のセット。ピン ON 時は SetSnapshot をスキップ。</summary>
    public HashSet<string> PinnedSnapshotNodeIds { get; } = new HashSet<string>();

    public WebSocketSession(WebSocket ws) { WebSocket = ws; }

    public async Task SendAsync(string message)
    {
        if (WebSocket.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(message);
        await _sendLock.WaitAsync();
        try
        {
            // ロック取得後に再チェック（コンパイル待機中に切断される race condition 対策）
            if (WebSocket.State != WebSocketState.Open) return;
            await WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (WebSocketException) { /* 切断済みセッションはスキップ */ }
        finally { _sendLock.Release(); }
    }
}

// ---- 保留実行リクエスト ----

/// <summary>起動時自動実行用のダミーセッション（WS 応答先なし）。</summary>
internal sealed class StartupNullSession : ISession
{
    public static readonly StartupNullSession Instance = new();
    private StartupNullSession() { }

    public NotifyingSnapshotStore SnapshotStore { get; } = new();
    public HashSet<string> PinnedSnapshotNodeIds { get; } = new();
    public Task SendAsync(string message) => Task.CompletedTask;
}

internal sealed class PendingExecution
{
    public ISession Session { get; }
    public NodeGraph Graph { get; }

    /// <summary>null の場合は全ノード実行（execute_graph）。</summary>
    public string? FragmentId { get; }

    /// <summary>true の場合は全断片をトポロジカル順に実行（execute_all_fragments）。</summary>
    public bool ExecuteAll { get; }

    public HashSet<string> PinnedFragmentIds { get; }

    public PendingExecution(ISession session, NodeGraph graph,
        string? fragmentId = null, bool executeAll = false, HashSet<string>? pinnedIds = null)
    {
        Session = session;
        Graph = graph;
        FragmentId = fragmentId;
        ExecuteAll = executeAll;
        PinnedFragmentIds = pinnedIds ?? new HashSet<string>();
    }
}
