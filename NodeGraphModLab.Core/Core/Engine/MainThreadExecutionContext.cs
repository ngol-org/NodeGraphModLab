using System.Collections.Concurrent;
using NodeGraphModLab.Core.Extensions;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Core.Engine;

/// <summary>
/// ホストのメインスレッド上で動作する実行コンテキスト。
/// MainThreadDispatch によりスレッドセーフにホスト内部状態へアクセスできる。
/// </summary>
public sealed class MainThreadExecutionContext : IExecutionContext
{
    private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private readonly string _nodeInstanceId;
    private readonly Dictionary<string, object?> _portValues = new();
    private readonly INodeLogger _logger;
    private readonly PersistentNodeRunner _runner;
    private readonly NodeRegistry? _registry;
    private readonly IKVStore? _store;
    private readonly ILiveParamStore? _liveParamStore;
    private readonly ExtensionServiceRegistry? _extensionServices;

    public INodeLogger Logger => _logger;
    public string NodeInstanceId => _nodeInstanceId;
    public ISnapshotStore? SnapshotStore => null;
    public PersistentNodeRunner Runner => _runner;
    public ILiveParamStore? LiveParamStore => _liveParamStore;

    public MainThreadExecutionContext(
        string nodeInstanceId,
        INgolLogger logger,
        PersistentNodeRunner runner,
        NodeRegistry? registry = null,
        IKVStore? store = null,
        ILiveParamStore? liveParamStore = null,
        ExtensionServiceRegistry? extensionServices = null)
    {
        _nodeInstanceId = nodeInstanceId;
        _logger = new NgolNodeLogger(logger);
        _runner = runner;
        _registry = registry;
        _store = store;
        _liveParamStore = liveParamStore;
        _extensionServices = extensionServices;
    }

    /// <summary>
    /// ホストのメインスレッドキューにアクションを追加する。
    /// DrainMainThreadQueue() の呼び出し元でデキューされる。
    /// </summary>
    public void MainThreadDispatch(Action action)
    {
        _mainThreadQueue.Enqueue(action);
    }

    /// <summary>外部スレッドからメインスレッドキューにアクションを追加する。</summary>
    public static void QueueAction(Action action) => _mainThreadQueue.Enqueue(action);

    /// <summary>ホストのメインスレッドから呼び出す。溜まったアクションを実行する。</summary>
    public static void DrainMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch { /* 個別ノードのエラーは隔離 */ }
        }
    }

    public object? GetPortValue(string portName)
    {
        _portValues.TryGetValue(portName, out var value);
        return value;
    }

    public void SetPortValue(string portName, object? value)
    {
        _portValues[portName] = value;
    }

    public T? GetParam<T>(string paramName) => default;

    public T GetLiveParam<T>(string key, T defaultValue = default!)
    {
        if (_liveParamStore == null || !_liveParamStore.TryGet(_nodeInstanceId, key, out var raw))
            return defaultValue;
        return global::NodeGraphModLab.NodeAPI.LiveParamStore.ConvertValue(raw, defaultValue);
    }

    /// <summary>
    /// ホストの更新サイクルごとに呼ばれるコールバックを登録します。
    /// PersistentNodeRunner を通じて管理されます。
    /// </summary>
    public IPersistentRegistration RegisterPersistent(PersistentCallbacks callbacks)
    {
        return _runner.Register(_nodeInstanceId, displayName: "", graphName: "", callbacks);
    }

    /// <summary>エンジンベースコンテキストでは下流接続情報を持たないため空を返す。</summary>
    public IReadOnlyList<PortConnection> GetDownstreamConnections(string outputPortName)
        => Array.Empty<PortConnection>();

    /// <summary>エンジンベースコンテキストでは SnapshotStore がないため no-op。</summary>
    public void PushLiveValue(string portName, object? value) { }

    public IKVStore Store => _store ?? throw new InvalidOperationException("KVStore not initialized");

    public T? GetExtensionService<T>() where T : class
        => _extensionServices?.GetService<T>();

    /// <summary>
    /// 指定の入力ポートタイプを持つノード一覧を返す。
    /// NodeRegistry が設定されていない場合は空リストを返す。
    /// </summary>
    public IReadOnlyList<NodeQuickLaunchInfo> GetNodesByInputPortType(string portType)
    {
        if (_registry == null) return Array.Empty<NodeQuickLaunchInfo>();
        var result = new List<NodeQuickLaunchInfo>();
        foreach (var d in _registry.GetAll())
        {
            foreach (var p in d.Ports)
            {
                if (p.Direction == PortDirection.Input &&
                    string.Equals(p.DataType, portType, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new NodeQuickLaunchInfo
                    {
                        TypeId = d.Id,
                        DisplayName = d.DisplayName,
                        Category = d.Category,
                        InputPortName = p.Name
                    });
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 指定ノードタイプを指定入力で単独実行する（クイック起動）。
    /// NodeRegistry が設定されていない場合は no-op。
    /// </summary>
    public void QuickExecuteNode(string nodeTypeId, string inputPortName, object? inputValue)
    {
        if (_registry == null) return;
        var node = _registry.CreateInstance(nodeTypeId);
        if (node == null)
        {
            _logger.LogWarning("[QuickExecute] Node not found: " + nodeTypeId);
            return;
        }
        var inputs = new Dictionary<string, object?> { [inputPortName] = inputValue };
        var ctx = new InlineExecutionContext(
            instanceId: "quick_" + nodeTypeId,
            paramValues: new Dictionary<string, System.Text.Json.JsonElement>(),
            inputValues: inputs,
            parent: this,
            liveParamStore: _liveParamStore,
            runner: _runner);
        try
        {
            node.Execute(ctx);
            _logger.LogInfo("[QuickExecute] Executed: " + nodeTypeId);
        }
        catch (Exception ex)
        {
            _logger.LogError("[QuickExecute] Error executing " + nodeTypeId + ": " + ex.Message);
        }
    }
}

/// <summary>
/// INgolLogger をラップした INodeLogger 実装。
/// </summary>
public sealed class NgolNodeLogger : INodeLogger
{
    private readonly INgolLogger _log;

    public NgolNodeLogger(INgolLogger log) => _log = log;

    public void LogInfo(string message)    => _log.LogInfo(message);
    public void LogWarning(string message) => _log.LogWarning(message);
    public void LogError(string message)   => _log.LogError(message);
    public void LogDebug(string message)   => _log.LogDebug(message);
}
