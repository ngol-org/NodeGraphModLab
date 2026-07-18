using System.Linq;
using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Core.Engine;

/// <summary>
/// GraphExecutor が内部で生成するノード実行コンテキスト。
/// </summary>
internal sealed class InlineExecutionContext : IExecutionContext
{
    private readonly Dictionary<string, object?> _inputValues;
    private readonly Dictionary<string, JsonElement> _paramValues;
    private readonly IExecutionContext _parent;
    private readonly ILiveParamStore? _liveParamStore;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<PortConnection>> _downstreamMap;
    private readonly PersistentNodeRunner? _runner;
    private readonly string _displayName;
    private readonly string _graphName;

    public string NodeInstanceId { get; }
    public INodeLogger Logger { get; }
    public Dictionary<string, object?> OutputValues { get; } = new();
    public ISnapshotStore? SnapshotStore { get; }

    public event Action<LogEntry>? OnLog;

    public InlineExecutionContext(
        string instanceId,
        Dictionary<string, JsonElement> paramValues,
        Dictionary<string, object?> inputValues,
        IExecutionContext parent,
        ISnapshotStore? snapshotStore = null,
        IReadOnlyDictionary<string, IReadOnlyList<PortConnection>>? downstreamMap = null,
        PersistentNodeRunner? runner = null,
        ILiveParamStore? liveParamStore = null,
        string displayName = "",
        string graphName = "")
    {
        NodeInstanceId = instanceId;
        _paramValues = paramValues;
        _inputValues = inputValues;
        _parent = parent;
        SnapshotStore = snapshotStore;
        _liveParamStore = liveParamStore ?? (parent as MainThreadExecutionContext)?.LiveParamStore;
        _downstreamMap = downstreamMap ?? new Dictionary<string, IReadOnlyList<PortConnection>>();
        _runner = runner;
        _displayName = displayName;
        _graphName = graphName;
        // Logger: OnLog イベントを発火しつつホスト側のログにも出力するプロキシを生成
        Logger = new InlineNodeLogger(this, parent.Logger);
    }

    public void MainThreadDispatch(Action action) => _parent.MainThreadDispatch(action);

    public object? GetPortValue(string portName)
    {
        if (_inputValues.TryGetValue(portName, out var value) && value != null) return value;
        // 未接続（または接続先が null を出力した）場合は paramValues（WebUIで直接入力された値）にフォールバックする
        if (_paramValues.TryGetValue(portName, out var elem)) return JsonElementToObject(elem);
        return null;
    }

    /// <summary>
    /// paramValues の JsonElement を GetPortValue の戻り値として使えるネイティブ型に変換する。
    /// Array は要素を再帰変換した List&lt;object?&gt; にする。
    /// Object 型はここでは変換せず null を返す（ノード側で ctx.GetParam&lt;T&gt; を使う）。
    /// </summary>
    internal static object? JsonElementToObject(JsonElement elem) => elem.ValueKind switch
    {
        JsonValueKind.String => elem.GetString(),
        JsonValueKind.Number => elem.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => elem.EnumerateArray().Select(JsonElementToObject).ToList(),
        _ => null,
    };

    public void SetPortValue(string portName, object? value)
    {
        OutputValues[portName] = value;
    }

    public T? GetParam<T>(string paramName)
    {
        if (!_paramValues.TryGetValue(paramName, out var elem)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(elem.GetRawText());
        }
        catch
        {
            return default;
        }
    }

    public T GetLiveParam<T>(string key, T defaultValue = default!)
    {
        if (_liveParamStore == null || !_liveParamStore.TryGet(NodeInstanceId, key, out var raw))
            return defaultValue;
        return global::NodeGraphModLab.NodeAPI.LiveParamStore.ConvertValue(raw, defaultValue);
    }

    public IPersistentRegistration RegisterPersistent(PersistentCallbacks callbacks)
    {
        if (_runner != null) return _runner.Register(NodeInstanceId, _displayName, _graphName, callbacks);
        return _parent.RegisterPersistent(callbacks);
    }

    /// <summary>指定出力ポートに接続された下流入力ポートの一覧を返す。</summary>
    public IReadOnlyList<PortConnection> GetDownstreamConnections(string outputPortName)
    {
        if (_downstreamMap.TryGetValue(outputPortName, out var list)) return list;
        return Array.Empty<PortConnection>();
    }

    /// <summary>
    /// 接続先の下流ノードの SnapshotStore スロットへ値をライブ書き込みし、
    /// LiveNotifications キューに通知エントリを積む。
    /// </summary>
    public void PushLiveValue(string portName, object? value)
    {
        if (SnapshotStore == null) return;

        foreach (var conn in GetDownstreamConnections(portName))
            SnapshotStore.PushLiveToSnapshot(conn.NodeInstanceId, conn.PortName, value);

        SnapshotStore.PushLive(NodeInstanceId, portName, value);
    }

    /// <summary>親コンテキストに委譲する。</summary>
    public IReadOnlyList<NodeQuickLaunchInfo> GetNodesByInputPortType(string portType)
        => _parent.GetNodesByInputPortType(portType);

    /// <summary>親コンテキストに委譲する。</summary>
    public void QuickExecuteNode(string nodeTypeId, string inputPortName, object? inputValue)
        => _parent.QuickExecuteNode(nodeTypeId, inputPortName, inputValue);

    /// <summary>親コンテキストに委譲する（全コンテキストで同一インスタンスを参照）。</summary>
    public NodeGraphModLab.NodeAPI.IKVStore Store => _parent.Store;

    public T? GetExtensionService<T>() where T : class => _parent.GetExtensionService<T>();

    // ---- 内部クラス ----

    /// <summary>
    /// ctx.Logger 経由のログを OnLog イベントに転送しつつホスト側のログにも出力するプロキシ。
    /// これにより MainThreadDispatch 内での ctx.Logger.LogXxx() が WebSocket ログにも届く。
    /// </summary>
    private sealed class InlineNodeLogger : INodeLogger
    {
        private readonly InlineExecutionContext _ctx;
        private readonly INodeLogger _hostLogger;

        internal InlineNodeLogger(InlineExecutionContext ctx, INodeLogger hostLogger)
        {
            _ctx = ctx;
            _hostLogger = hostLogger;
        }

        public void LogInfo(string message)
        {
            _hostLogger.LogInfo(message);
            _ctx.OnLog?.Invoke(new LogEntry { NodeInstanceId = _ctx.NodeInstanceId, Message = message, Level = LogLevel.Info });
        }

        public void LogWarning(string message)
        {
            _hostLogger.LogWarning(message);
            _ctx.OnLog?.Invoke(new LogEntry { NodeInstanceId = _ctx.NodeInstanceId, Message = message, Level = LogLevel.Warning });
        }

        public void LogError(string message)
        {
            _hostLogger.LogError(message);
            _ctx.OnLog?.Invoke(new LogEntry { NodeInstanceId = _ctx.NodeInstanceId, Message = message, Level = LogLevel.Error });
        }

        public void LogDebug(string message)
        {
            _hostLogger.LogDebug(message);
            _ctx.OnLog?.Invoke(new LogEntry { NodeInstanceId = _ctx.NodeInstanceId, Message = message, Level = LogLevel.Debug });
        }
    }
}
