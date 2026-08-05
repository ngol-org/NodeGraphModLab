using System.Collections.Concurrent;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.Core.Extensions;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

internal sealed class HandlerContext
{
    public NodeRegistry Registry { get; }
    public ILiveParamStore LiveParamStore { get; }
    public IDebugLogStore DebugLogStore { get; }
    public INgolLogger? Log { get; }
    public string GraphSaveDir { get; }
    public string DynamicNodesDir { get; }
    public string NodesDir { get; }
    public string NodePacksDir { get; }
    public ConcurrentDictionary<string, string> ScriptNodeId { get; }
    public ConcurrentQueue<PendingExecution> PendingExecutions { get; }
    public PersistentNodeRunner Runner { get; }
    public Func<CancellationTokenSource?> GetExecutionCts { get; }
    public GraphExecutor Executor { get; }
    public ExtensionServiceRegistry? ExtensionServices { get; }
    public IKVStore? Store { get; }
    /// <summary>target で選んだブラウザへグラフを開くよう指示し、送れた宛先を返す。</summary>
    public Func<string, string, Task<List<BrowserTargetInfo>>> SendOpenGraphToBrowsers { get; }
    /// <summary>target で選んだブラウザへリロードを指示し、送れた宛先を返す。</summary>
    public Func<bool, string, Task<List<BrowserTargetInfo>>> SendReloadToBrowsers { get; }
    /// <summary>保存せずに一時登録されたグラフ。プロセスが生きている間だけ保持する。</summary>
    internal TemporaryGraphStore TemporaryGraphs { get; } = new();
    public HotReloadGate HotReloadGate { get; }

    public HandlerContext(
        NodeRegistry registry,
        ILiveParamStore liveParamStore,
        IDebugLogStore debugLogStore,
        INgolLogger? log,
        string graphSaveDir,
        string dynamicNodesDir,
        string nodesDir,
        string nodePacksDir,
        ConcurrentDictionary<string, string> scriptNodeId,
        ConcurrentQueue<PendingExecution> pendingExecutions,
        PersistentNodeRunner runner,
        Func<CancellationTokenSource?> getExecutionCts,
        GraphExecutor executor,
        ExtensionServiceRegistry? extensionServices = null,
        IKVStore? store = null,
        Func<string, string, Task<List<BrowserTargetInfo>>>? sendOpenGraphToBrowsers = null,
        HotReloadGate? hotReloadGate = null,
        Func<bool, string, Task<List<BrowserTargetInfo>>>? sendReloadToBrowsers = null)
    {
        Registry = registry;
        LiveParamStore = liveParamStore;
        DebugLogStore = debugLogStore;
        Log = log;
        GraphSaveDir = graphSaveDir;
        DynamicNodesDir = dynamicNodesDir;
        NodesDir = nodesDir;
        NodePacksDir = nodePacksDir;
        ScriptNodeId = scriptNodeId;
        PendingExecutions = pendingExecutions;
        Runner = runner;
        GetExecutionCts = getExecutionCts;
        Executor = executor;
        ExtensionServices = extensionServices;
        Store = store;
        SendOpenGraphToBrowsers = sendOpenGraphToBrowsers ?? ((_, _) => Task.FromResult(new List<BrowserTargetInfo>()));
        SendReloadToBrowsers = sendReloadToBrowsers ?? ((_, _) => Task.FromResult(new List<BrowserTargetInfo>()));
        HotReloadGate = hotReloadGate ?? new HotReloadGate();
    }
}
