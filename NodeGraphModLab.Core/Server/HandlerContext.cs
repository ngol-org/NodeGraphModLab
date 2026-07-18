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
    public Func<string, Task<bool>> SendOpenGraphToLatestBrowser { get; }

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
        Func<string, Task<bool>>? sendOpenGraphToLatestBrowser = null)
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
        SendOpenGraphToLatestBrowser = sendOpenGraphToLatestBrowser ?? (async _ => false);
    }
}
