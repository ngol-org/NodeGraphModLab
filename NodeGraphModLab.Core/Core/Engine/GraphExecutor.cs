using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Core.Engine;

/// <summary>
/// 単体ノード実行結果。
/// </summary>
public sealed class SingleNodeExecutionResult
{
    public bool Success          { get; init; }
    public string? ErrorMessage  { get; init; }
    public Dictionary<string, object?> Outputs { get; init; } = new();
    public List<LogEntry> Logs   { get; init; } = new();
    public TimeSpan Duration     { get; init; }

    /// <summary>ExecuteSingleNode が生成した一時ノードインスタンスID（"rn_..."）。永続ノードJobの紐付け検索に使う。</summary>
    public string InstanceId     { get; init; } = "";
}

/// <summary>
/// グラフ実行結果。
/// </summary>
public sealed class ExecutionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<LogEntry> Logs { get; init; } = new();
    public TimeSpan Duration { get; init; }
}

public sealed class LogEntry
{
    public string NodeInstanceId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public LogLevel Level { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// グラフ実行エンジン。
/// トポロジカルソートで実行順序を決定し、ノードを順次実行する。
/// 失敗したノードは隔離して後続も継続実行する。
/// </summary>
public sealed class GraphExecutor
{
    private readonly NodeRegistry _registry;

    /// <summary>
    /// ノード実行進捗コールバック。(nodeInstanceId, status) を受け取る。
    /// status: "running" | "done" | "error"
    /// </summary>
    public Action<string, string, double>? OnNodeProgress { get; set; }

    /// <summary>
    /// ノード単体の実行タイムアウト (デフォルト 30 秒)。
    /// </summary>
    public TimeSpan NodeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public GraphExecutor(NodeRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// グラフを実行する。
    /// <paramref name="snapshotStore"/> を省略（null）すると、ノード側の
    /// <c>ctx.SnapshotStore</c> が null のまま実行される（PushLiveValue/SetSnapshot は無音でno-op）。
    /// セッション付きの実行（WebSocket 経由の Execute ボタン等）では呼び出し元のセッションが持つ
    /// 実 SnapshotStore を渡すこと（断片実行系と同じ扱いにするため）。
    /// </summary>
    public ExecutionResult Execute(
        NodeGraph graph,
        IExecutionContext baseContext,
        CancellationToken cancellationToken = default,
        ISnapshotStore? snapshotStore = null)
    {
        var startTime = DateTimeOffset.UtcNow;
        var logs = new List<LogEntry>();

        if (graph.Nodes.Count == 0)
        {
            return new ExecutionResult { Success = true, Logs = logs, Duration = TimeSpan.Zero };
        }

        // トポロジカルソート
        var (sortedIds, cyclicError) = GraphTopologyHelper.TopologicalSort(graph);
        if (cyclicError != null)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = cyclicError,
                Logs = logs,
                Duration = DateTimeOffset.UtcNow - startTime
            };
        }

        // ノードインスタンスマップ
        var instanceMap = graph.Nodes.ToDictionary(n => n.InstanceId);

        // ポート値キャッシュ
        var portValues = new Dictionary<(string instanceId, string portName), object?>();

        // ForEach コントローラーノードがあればループ展開込みで実行する
        if (TryExecuteForEachBranch(graph, sortedIds, instanceMap, portValues, baseContext, logs, cancellationToken, snapshotStore: snapshotStore, out var forEachHasError))
        {
            return new ExecutionResult
            {
                Success = !forEachHasError,
                Logs = logs,
                Duration = DateTimeOffset.UtcNow - startTime
            };
        }

        // ---- 通常実行（ForEachなし） ----
        {
            var hasError = false;
            foreach (var instanceId in sortedIds)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    logs.Add(new LogEntry
                    {
                        NodeInstanceId = "",
                        Message = "[ABORT] Execution was cancelled.",
                        Level = LogLevel.Warning
                    });
                    return new ExecutionResult
                    {
                        Success = false,
                        ErrorMessage = "Execution cancelled.",
                        Logs = logs,
                        Duration = DateTimeOffset.UtcNow - startTime
                    };
                }

                var ok = ExecuteNode(instanceId, graph, instanceMap, portValues, baseContext, logs, cancellationToken, snapshotStore: snapshotStore);
                if (!ok) hasError = true;
            }

            return new ExecutionResult
            {
                Success = !hasError,
                Logs = logs,
                Duration = DateTimeOffset.UtcNow - startTime
            };
        }
    }

    // ---- 内部ヘルパー ----

    /// <summary>ソート済みIDリスト内の最初の ForEach コントローラーのインデックスを返す。</summary>
    private int FindForEachIndex(List<string> sortedIds, Dictionary<string, NodeInstance> instanceMap)
    {
        for (int i = 0; i < sortedIds.Count; i++)
        {
            var id = sortedIds[i];
            if (!instanceMap.TryGetValue(id, out var ni)) continue;
            var desc = _registry.Get(ni.NodeTypeId);
            if (desc == null) continue;
            if (typeof(IForEachController).IsAssignableFrom(desc.ImplementationType))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// ForEach の current_item/index 出力ポートから接続を辿った推移閉包（真のループ本体）を返す。
    /// count 等イテレーションで値が変わらないポートにしか依存しないノードはここに含まれない。
    /// </summary>
    private static HashSet<string> ComputeLoopBodyClosure(
        string forEachId,
        string itemPortName,
        string indexPortName,
        List<NodeConnection> connections)
    {
        var perIterationPorts = new HashSet<string> { itemPortName, indexPortName };
        var closure = new HashSet<string>();
        var frontier = new Queue<string>();

        foreach (var conn in connections.Where(c => c.FromNodeInstanceId == forEachId && perIterationPorts.Contains(c.FromPortName)))
        {
            if (closure.Add(conn.ToNodeInstanceId)) frontier.Enqueue(conn.ToNodeInstanceId);
        }

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var conn in connections.Where(c => c.FromNodeInstanceId == current))
            {
                if (closure.Add(conn.ToNodeInstanceId)) frontier.Enqueue(conn.ToNodeInstanceId);
            }
        }

        return closure;
    }

    /// <summary>
    /// loopBodyIds（ループ本体）が依存している candidateIds 内のノードを逆方向 BFS で収集する。
    /// これらは current_item/index には依存しないが、ループ本体の入力として毎イテレーション
    /// 必要になるため、ループ開始前に1回だけ実行する必要がある。
    /// forEachId 自身・既に loopBodyClosure に含まれるノード・candidateIds 外（= preLoopIds、
    /// 既に実行済み）は対象から除外する。
    /// </summary>
    private static HashSet<string> ComputeLoopInputIds(
        string forEachId,
        List<string> loopBodyIds,
        HashSet<string> loopBodyClosure,
        HashSet<string> candidateSet,
        List<NodeConnection> connections)
    {
        var loopInputIds = new HashSet<string>();
        var frontier = new Queue<string>(loopBodyIds);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var conn in connections.Where(c => c.ToNodeInstanceId == current))
            {
                var srcId = conn.FromNodeInstanceId;
                if (srcId == forEachId) continue;
                if (loopBodyClosure.Contains(srcId)) continue;
                if (!candidateSet.Contains(srcId)) continue;
                if (loopInputIds.Add(srcId)) frontier.Enqueue(srcId);
            }
        }

        return loopInputIds;
    }

    /// <summary>
    /// sortedIds に ForEach コントローラーが含まれる場合、ループ展開込みで実行して true を返す
    /// （hasError にエラー有無を格納）。含まれない場合は何もせず false を返す
    /// （呼び出し元が通常のノード実行ループを行う）。
    /// Execute() と ExecuteFragmentInternal() の双方から共有される。
    /// </summary>
    private bool TryExecuteForEachBranch(
        NodeGraph graph,
        List<string> sortedIds,
        Dictionary<string, NodeInstance> instanceMap,
        Dictionary<(string, string), object?> portValues,
        IExecutionContext baseContext,
        List<LogEntry> logs,
        CancellationToken cancellationToken,
        ISnapshotStore? snapshotStore,
        out bool hasError)
    {
        hasError = false;

        var forEachIndex = FindForEachIndex(sortedIds, instanceMap);
        if (forEachIndex < 0) return false;

        var preLoopIds   = sortedIds.Take(forEachIndex).ToList();
        var forEachId    = sortedIds[forEachIndex];
        var candidateIds = sortedIds.Skip(forEachIndex + 1).ToList();

        // ForEachより前のノードを実行
        foreach (var id in preLoopIds)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var ok = ExecuteNode(id, graph, instanceMap, portValues, baseContext, logs, cancellationToken, snapshotStore: snapshotStore);
            if (!ok) hasError = true;
        }

        // ForEach ノード自体を実行してアイテムリストを取得
        ExecuteNode(forEachId, graph, instanceMap, portValues, baseContext, logs, cancellationToken, snapshotStore: snapshotStore);

        var forEachNodeInstance = instanceMap[forEachId];
        var forEachDescriptor   = _registry.Get(forEachNodeInstance.NodeTypeId);
        var forEachObj          = _registry.CreateInstance(forEachNodeInstance.NodeTypeId) as IForEachController;

        if (forEachObj != null && forEachDescriptor != null)
        {
            var preInputValues = ResolveInputValues(forEachId, graph.Connections, portValues);
            // items が未接続の場合は paramValues（WebUIで直接入力されたリテラル配列）にフォールバックする
            if ((!preInputValues.TryGetValue("items", out var itemsVal) || itemsVal == null)
                && forEachNodeInstance.ParamValues.TryGetValue("items", out var itemsElem))
            {
                preInputValues["items"] = InlineExecutionContext.JsonElementToObject(itemsElem);
            }

            var items = forEachObj.GetItems(preInputValues);
            var itemPortName  = forEachObj.CurrentItemPortName;
            var indexPortName = forEachObj.IndexPortName;

            // current_item/index への依存を辿った推移閉包だけを真のループ本体とする。
            // count 等イテレーションで値が変わらないポートにしか依存しないノードは
            // ループ完了後に1回だけ実行する（postLoopIds）。
            var loopBodyClosure = ComputeLoopBodyClosure(forEachId, itemPortName, indexPortName, graph.Connections);
            var loopBodyIds = candidateIds.Where(id => loopBodyClosure.Contains(id)).ToList();

            // ループ本体が依存している独立ノード（current_item/index には依存しないが
            // loopBodyIds への入力になっているもの）は、ループ開始前に1回だけ実行する必要がある。
            // トポロジカルソートは依存の無いノード同士の前後関係を保証しないため、
            // ForEach より後ろに並んだだけで postLoopIds に紛れ込むケースがある。
            var candidateSet = candidateIds.ToHashSet();
            var loopInputIds = ComputeLoopInputIds(forEachId, loopBodyIds, loopBodyClosure, candidateSet, graph.Connections);
            var loopInputIdList = candidateIds.Where(id => loopInputIds.Contains(id)).ToList();
            var postLoopIds = candidateIds.Where(id => !loopBodyClosure.Contains(id) && !loopInputIds.Contains(id)).ToList();

            logs.Add(new LogEntry
            {
                NodeInstanceId = forEachId,
                Message = $"[ForEach] {forEachDescriptor.DisplayName}: {items.Count} items",
                Level = LogLevel.Info
            });

            // ループ本体が依存する独立ノードをループ開始前に1回だけ実行
            foreach (var id in loopInputIdList)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var ok = ExecuteNode(id, graph, instanceMap, portValues, baseContext, logs, cancellationToken, snapshotStore: snapshotStore);
                if (!ok) hasError = true;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // 現在アイテムをポートキャッシュに設定
                portValues[(forEachId, itemPortName)]  = items[i];
                portValues[(forEachId, indexPortName)] = (double)i;

                // ループ本体を実行
                foreach (var bodyId in loopBodyIds)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var ok = ExecuteNode(bodyId, graph, instanceMap, portValues, baseContext, logs, cancellationToken, loopIteration: i, snapshotStore: snapshotStore);
                    if (!ok) hasError = true;
                }
            }

            // ループに依存しない後続ノードは、items が0件でも必ず1回だけ実行する
            foreach (var id in postLoopIds)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var ok = ExecuteNode(id, graph, instanceMap, portValues, baseContext, logs, cancellationToken, snapshotStore: snapshotStore);
                if (!ok) hasError = true;
            }
        }

        return true;
    }

    /// <summary>単一ノードを実行し、成功/失敗を返す。</summary>
    private bool ExecuteNode(
        string instanceId,
        NodeGraph graph,
        Dictionary<string, NodeInstance> instanceMap,
        Dictionary<(string, string), object?> portValues,
        IExecutionContext baseContext,
        List<LogEntry> logs,
        CancellationToken cancellationToken,
        int loopIteration = -1,
        ISnapshotStore? snapshotStore = null)
    {
        if (!instanceMap.TryGetValue(instanceId, out var nodeInstance)) return true;

        var descriptor = _registry.Get(nodeInstance.NodeTypeId);
        if (descriptor == null)
        {
            logs.Add(new LogEntry
            {
                NodeInstanceId = instanceId,
                Message = $"[SKIP] Unknown node type: {nodeInstance.NodeTypeId}",
                Level = LogLevel.Warning
            });
            return true;
        }

        var nodeObj = _registry.CreateInstance(nodeInstance.NodeTypeId);
        if (nodeObj == null)
        {
            logs.Add(new LogEntry
            {
                NodeInstanceId = instanceId,
                Message = $"[ERROR] Failed to create node: {nodeInstance.NodeTypeId}",
                Level = LogLevel.Error
            });
            return false;
        }

        // 入力ポートに接続先の値を注入
        var inputValues = ResolveInputValues(instanceId, graph.Connections, portValues);

        // 下流接続マップを構築（PushLiveValue のために渡す）
        var downstreamMap = BuildDownstreamMap(instanceId, graph.Connections);

        var runner = (baseContext as MainThreadExecutionContext)?.Runner;
        var ctx = new InlineExecutionContext(
            instanceId,
            nodeInstance.ParamValues,
            inputValues,
            baseContext,
            snapshotStore,
            downstreamMap,
            runner,
            displayName: descriptor.DisplayName,
            graphName: graph.Name
        );
        ctx.OnLog += entry => logs.Add(entry);

        try
        {
            var nodeStartTime = DateTimeOffset.UtcNow;
            OnNodeProgress?.Invoke(instanceId, "running", 0);

            nodeObj.Execute(ctx);

            var nodeDurationMs = (DateTimeOffset.UtcNow - nodeStartTime).TotalMilliseconds;
            OnNodeProgress?.Invoke(instanceId, "done", nodeDurationMs);

            // 出力ポートの値をキャッシュ
            foreach (var (portName, value) in ctx.OutputValues)
            {
                portValues[(instanceId, portName)] = value;
            }

            var iterStr = loopIteration >= 0 ? $" (iteration {loopIteration})" : "";
            logs.Add(new LogEntry
            {
                NodeInstanceId = instanceId,
                Message = $"[OK] {descriptor.DisplayName}{iterStr}",
                Level = LogLevel.Info
            });
            return true;
        }
        catch (Exception ex)
        {
            OnNodeProgress?.Invoke(instanceId, "error", 0);
            logs.Add(new LogEntry
            {
                NodeInstanceId = instanceId,
                Message = $"[ERROR] {descriptor.DisplayName}: {ex.Message}",
                Level = LogLevel.Error
            });
            return false;
        }
    }

    private Dictionary<string, object?> ResolveInputValues(
        string toNodeId,
        List<NodeConnection> connections,
        Dictionary<(string, string), object?> portValues)
    {
        var result = new Dictionary<string, object?>();
        foreach (var conn in connections.Where(c => c.ToNodeInstanceId == toNodeId))
        {
            if (portValues.TryGetValue((conn.FromNodeInstanceId, conn.FromPortName), out var value))
            {
                result[conn.ToPortName] = value;
            }
        }
        return result;
    }

    /// <summary>
    /// 指定ノードの出力ポートから接続された下流入力ポートのマップを構築する。
    /// key = 出力ポート名, value = 接続先 (nodeId, portName) リスト
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<PortConnection>> BuildDownstreamMap(
        string fromNodeId,
        List<NodeConnection> connections)
    {
        var map = new Dictionary<string, List<PortConnection>>();
        foreach (var conn in connections)
        {
            if (conn.FromNodeInstanceId != fromNodeId) continue;
            if (!map.TryGetValue(conn.FromPortName, out var list))
            {
                list = new List<PortConnection>();
                map[conn.FromPortName] = list;
            }
            list.Add(new PortConnection(conn.ToNodeInstanceId, conn.ToPortName));
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<PortConnection>)kv.Value);
    }

    // ================================================================
    // 単体ノード実行 API
    // ================================================================

    /// <summary>グラフを組まずに単一ノードを実行し、出力ポート値を返す。</summary>
    public SingleNodeExecutionResult ExecuteSingleNode(
        string nodeTypeId,
        Dictionary<string, JsonElement> paramValues,
        Dictionary<string, object?> inputValues,
        IExecutionContext baseContext,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var logs = new List<LogEntry>();

        var descriptor = _registry.Get(nodeTypeId);
        if (descriptor == null)
            return new SingleNodeExecutionResult
            {
                Success = false,
                ErrorMessage = $"Unknown node type: {nodeTypeId}",
                Duration = DateTimeOffset.UtcNow - startTime
            };

        var nodeObj = _registry.CreateInstance(nodeTypeId);
        if (nodeObj == null)
            return new SingleNodeExecutionResult
            {
                Success = false,
                ErrorMessage = $"Failed to create instance: {nodeTypeId}",
                Duration = DateTimeOffset.UtcNow - startTime
            };

        var instanceId = $"rn_{Guid.NewGuid():N}";
        var runner = (baseContext as MainThreadExecutionContext)?.Runner;
        var ctx = new InlineExecutionContext(
            instanceId,
            paramValues,
            inputValues,
            baseContext,
            snapshotStore: null,
            downstreamMap: null,
            runner: runner,
            displayName: descriptor.DisplayName,
            graphName: "(run_node)");
        ctx.OnLog += entry => logs.Add(entry);

        try
        {
            nodeObj.Execute(ctx);
            return new SingleNodeExecutionResult
            {
                Success = true,
                Outputs = ctx.OutputValues,
                Logs = logs,
                Duration = DateTimeOffset.UtcNow - startTime,
                InstanceId = instanceId
            };
        }
        catch (Exception ex)
        {
            logs.Add(new LogEntry
            {
                NodeInstanceId = instanceId,
                Message = $"[ERROR] {descriptor.DisplayName}: {ex.Message}",
                Level = LogLevel.Error
            });
            return new SingleNodeExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Outputs = ctx.OutputValues,
                Logs = logs,
                Duration = DateTimeOffset.UtcNow - startTime,
                InstanceId = instanceId
            };
        }
    }

    // ================================================================
    // Fragment 実行 API
    // ================================================================

    /// <summary>
    /// 指定 Fragment のみを実行する。
    /// FragmentLink で参照している上流 Fragment の snapshot が未保存かつピン留めなしの場合、
    /// 上流を再帰的に先実行する（カスケード）。
    /// </summary>
    public ExecutionResult ExecuteFragment(
        NodeGraph graph,
        string fragmentId,
        IExecutionContext baseContext,
        ISnapshotStore snapshotStore,
        ISet<string>? pinnedFragmentIds = null,
        CancellationToken cancellationToken = default)
    {
        var executed = new HashSet<string>();
        return ExecuteFragmentInternal(graph, fragmentId, baseContext, snapshotStore,
            pinnedFragmentIds ?? new HashSet<string>(), executed, cancellationToken);
    }

    private ExecutionResult ExecuteFragmentInternal(
        NodeGraph graph,
        string fragmentId,
        IExecutionContext baseContext,
        ISnapshotStore snapshotStore,
        ISet<string> pinnedFragmentIds,
        HashSet<string> alreadyExecuted,
        CancellationToken cancellationToken)
    {
        if (alreadyExecuted.Contains(fragmentId))
            return new ExecutionResult { Success = true, Logs = new(), Duration = TimeSpan.Zero };

        var fragment = graph.Fragments.FirstOrDefault(f => f.Id == fragmentId);
        if (fragment == null)
            return new ExecutionResult { Success = false, ErrorMessage = $"Fragment '{fragmentId}' not found.", Logs = new(), Duration = TimeSpan.Zero };

        var startTime = DateTimeOffset.UtcNow;
        var logs = new List<LogEntry>();

        // ---- カスケード: FragmentLink の上流が未スナップショットなら先実行 ----
        var nodeIdSet = fragment.NodeInstanceIds.ToHashSet();
        var upstreamFragIds = graph.FragmentLinks
            .Where(fl => nodeIdSet.Contains(fl.ToNodeInstanceId))
            .Select(fl => FindFragmentForNode(graph, fl.SourceSnapshotNodeInstanceId))
            .Where(fid => fid != null && fid != fragmentId)
            .Distinct()
            .ToList();

        foreach (var upFid in upstreamFragIds)
        {
            if (upFid == null) continue;
            if (pinnedFragmentIds.Contains(upFid)) continue;

            // 上流スナップショットが1つでも存在すれば自動実行不要（部分有効ポリシー）
            var upFragment = graph.Fragments.FirstOrDefault(f => f.Id == upFid);
            if (upFragment == null) continue;

            bool hasAnySnapshot = upFragment.NodeInstanceIds
                .Any(nid => graph.FragmentLinks
                    .Where(fl => fl.SourceSnapshotNodeInstanceId == nid)
                    .Any(fl => snapshotStore.HasSnapshot(nid, fl.SourcePortName)));

            if (!hasAnySnapshot)
            {
                var cascadeResult = ExecuteFragmentInternal(graph, upFid, baseContext, snapshotStore,
                    pinnedFragmentIds, alreadyExecuted, cancellationToken);
                if (!cascadeResult.Success)
                    logs.AddRange(cascadeResult.Logs);
            }
        }

        // ---- FragmentLink から外部入力を解決してポート値キャッシュに注入 ----
        var portValues = new Dictionary<(string, string), object?>();

        foreach (var fl in graph.FragmentLinks.Where(fl => nodeIdSet.Contains(fl.ToNodeInstanceId)))
        {
            if (snapshotStore.HasSnapshot(fl.SourceSnapshotNodeInstanceId, fl.SourcePortName))
            {
                var val = snapshotStore.GetSnapshot(fl.SourceSnapshotNodeInstanceId, fl.SourcePortName);
                // 仮想エッジとして portValues に注入: (ソースノードID, ポート名) → 値
                portValues[(fl.SourceSnapshotNodeInstanceId, fl.SourcePortName)] = val;

                // fragment 内で toNode の入力が直接 portValues から解決されるよう
                // 接続エントリも追加（仮想接続として扱う）
                // → ResolveInputValues はグラフの connections を使うので
                //   portValues に (sourceId, sourcePort) を入れておけば機能する
            }
        }

        // Fragment 内のノードと接続だけのサブグラフを構築
        var subGraph = BuildSubGraph(graph, fragment, portValues);

        // サブグラフ実行（既存 Execute ロジックを流用）
        var instanceMap = subGraph.graph.Nodes.ToDictionary(n => n.InstanceId);
        var (sortedIds, cyclicError) = GraphTopologyHelper.TopologicalSort(subGraph.graph);
        if (cyclicError != null)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = $"[Fragment {fragment.Name}] {cyclicError}",
                Logs = logs,
                Duration = DateTimeOffset.UtcNow - startTime
            };
        }

        bool hasError;
        if (!TryExecuteForEachBranch(subGraph.graph, sortedIds, instanceMap, subGraph.portValues,
                baseContext, logs, cancellationToken, snapshotStore, out hasError))
        {
            hasError = false;
            foreach (var instanceId in sortedIds)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var ok = ExecuteNode(instanceId, subGraph.graph, instanceMap, subGraph.portValues,
                    baseContext, logs, cancellationToken, snapshotStore: snapshotStore);
                if (!ok) hasError = true;
            }
        }

        alreadyExecuted.Add(fragmentId);
        return new ExecutionResult
        {
            Success = !hasError,
            Logs = logs,
            Duration = DateTimeOffset.UtcNow - startTime
        };
    }

    /// <summary>
    /// グラフの全 Fragment を断片間 DAG のトポロジカル順に実行する。
    /// pinnedFragmentIds に含まれる Fragment はスキップする。
    /// graph.Fragments が空かつ FragmentLinks が空の場合は既存の全ノード実行にフォールバック。
    /// graph.Fragments が空かつ FragmentLinks がある場合は接続から断片を自動導出して断片順実行。
    /// </summary>
    public ExecutionResult ExecuteAllFragments(
        NodeGraph graph,
        IExecutionContext baseContext,
        ISnapshotStore snapshotStore,
        ISet<string>? pinnedFragmentIds = null,
        CancellationToken cancellationToken = default)
    {
        if (graph.Fragments.Count == 0)
        {
            if (graph.FragmentLinks.Count == 0)
                return Execute(graph, baseContext, cancellationToken, snapshotStore);

            // fragmentLinks あり・fragments 未定義 → 接続から断片を自動導出して断片順実行
            var computed = GraphTopologyHelper.ComputeFragmentsFromConnections(graph);
            var graphWithFrags = new NodeGraph
            {
                Id            = graph.Id,
                Name          = graph.Name,
                Description   = graph.Description,
                SchemaVersion = graph.SchemaVersion,
                Version       = graph.Version,
                CreatedAt     = graph.CreatedAt,
                Nodes         = graph.Nodes,
                Connections   = graph.Connections,
                FragmentLinks  = graph.FragmentLinks,
                Groups        = graph.Groups,
                Annotations   = graph.Annotations,
                Fragments     = computed.ToList()
            };
            return ExecuteAllFragments(graphWithFrags, baseContext, snapshotStore,
                pinnedFragmentIds, cancellationToken);
        }

        var pinned = pinnedFragmentIds ?? new HashSet<string>();
        var (sortedFragIds, cyclicError) = GraphTopologyHelper.TopologicalSortFragments(graph);
        if (cyclicError != null)
            return new ExecutionResult { Success = false, ErrorMessage = cyclicError, Logs = new(), Duration = TimeSpan.Zero };

        var startTime = DateTimeOffset.UtcNow;
        var allLogs = new List<LogEntry>();
        var alreadyExecuted = new HashSet<string>();
        bool hasError = false;

        foreach (var fid in sortedFragIds)
        {
            if (pinned.Contains(fid)) continue;
            if (alreadyExecuted.Contains(fid)) continue;

            var result = ExecuteFragmentInternal(graph, fid, baseContext, snapshotStore,
                pinned, alreadyExecuted, cancellationToken);
            allLogs.AddRange(result.Logs);
            if (!result.Success) hasError = true;
        }

        return new ExecutionResult
        {
            Success = !hasError,
            Logs = allLogs,
            Duration = DateTimeOffset.UtcNow - startTime
        };
    }

    // ---- Fragment ヘルパー ----

    private static string? FindFragmentForNode(NodeGraph graph, string nodeInstanceId)
        => graph.Fragments.FirstOrDefault(f => f.NodeInstanceIds.Contains(nodeInstanceId))?.Id;

    private static (NodeGraph graph, Dictionary<(string, string), object?> portValues) BuildSubGraph(
        NodeGraph graph,
        FragmentDefinition fragment,
        Dictionary<(string, string), object?> externalPortValues)
    {
        var nodeIdSet = fragment.NodeInstanceIds.ToHashSet();
        var subNodes = graph.Nodes.Where(n => nodeIdSet.Contains(n.InstanceId)).ToList();
        var subConnections = graph.Connections
            .Where(c => nodeIdSet.Contains(c.FromNodeInstanceId) && nodeIdSet.Contains(c.ToNodeInstanceId))
            .ToList();

        // FragmentLink 由来の仮想接続（スナップショットノードID → 受け取るノード）を追加
        var virtualConnections = graph.FragmentLinks
            .Where(fl => nodeIdSet.Contains(fl.ToNodeInstanceId))
            .Select(fl => new NodeConnection
            {
                FromNodeInstanceId = fl.SourceSnapshotNodeInstanceId,
                FromPortName = fl.SourcePortName,
                ToNodeInstanceId = fl.ToNodeInstanceId,
                ToPortName = fl.ToPortName
            })
            .ToList();

        var subGraph = new NodeGraph
        {
            Id = graph.Id,
            Name = graph.Name,
            Nodes = subNodes,
            Connections = subConnections.Concat(virtualConnections).ToList()
        };

        return (subGraph, new Dictionary<(string, string), object?>(externalPortValues));
    }

}
