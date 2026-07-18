using System.Collections.Generic;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Core.Engine;
using NUnit.Framework;

namespace NodeGraphModLab.Tests;

/// <summary>
/// Phase 4: GraphExecutor.ExecuteFragment / ExecuteAllFragments のテスト。
/// - Fragment 内のノードのみ実行される
/// - FragmentLink → SnapshotStore → 後続 Fragment 入力注入
/// - カスケード自動実行（上流 snapshot が空なら上流を先実行）
/// - カスケード多重実行防止
/// - ピン留め Fragment はスキップ
/// - 全断片実行（ExecuteAllFragments）の順序
/// - fragments 空のグラフは従来通り全ノード実行
/// </summary>
[TestFixture]
public class FragmentExecutionTests
{
    // ---- ヘルパー ----

    private static NodeRegistry BuildRegistry()
    {
        var registry = new NodeRegistry();
        registry.RegisterAssembly(typeof(NodeGraphModLab.BuiltinNodes.Fragment.SnapshotAnyNode).Assembly);
        registry.RegisterAssembly(typeof(NodeGraphModLab.BuiltinNodes.Logic.AddNode).Assembly);
        return registry;
    }

    private static TestExecutionContext BaseCtx() => new TestExecutionContext("base");

    /// <summary>
    /// 指定 nodeTypeId のノードを持つ NodeInstance を生成する。
    /// </summary>
    private static NodeInstance Node(string instanceId, string nodeTypeId)
        => new NodeInstance { InstanceId = instanceId, NodeTypeId = nodeTypeId };

    private static NodeConnection Conn(string fromId, string fromPort, string toId, string toPort)
        => new NodeConnection { FromNodeInstanceId = fromId, FromPortName = fromPort, ToNodeInstanceId = toId, ToPortName = toPort };

    private static FragmentDefinition Frag(string id, string name, params string[] nodeIds)
        => new FragmentDefinition { Id = id, Name = name, NodeInstanceIds = nodeIds.ToList() };

    private static FragmentLink FLink(string snapNodeId, string toNodeId, string toPort, string sourcePort = "value")
        => new FragmentLink { SourceSnapshotNodeInstanceId = snapNodeId, SourcePortName = sourcePort, ToNodeInstanceId = toNodeId, ToPortName = toPort };

    // ================================================================
    // TopologicalSortFragments テスト
    // ================================================================

    [Test]
    public void TopologicalSortFragments_TwoFragments_CorrectOrder()
    {
        // Fragment A → (FragmentLink) → Fragment B
        var graph = new NodeGraph
        {
            Fragments =
            {
                Frag("fA", "A", "snap-node"),
                Frag("fB", "B", "target-node")
            },
            FragmentLinks =
            {
                FLink("snap-node", "target-node", "input")
            }
        };

        var (sorted, error) = GraphTopologyHelper.TopologicalSortFragments(graph);
        Assert.That(error, Is.Null);
        Assert.That(sorted.IndexOf("fA"), Is.LessThan(sorted.IndexOf("fB")));
    }

    [Test]
    public void TopologicalSortFragments_NoLinks_AllFragmentsPresent()
    {
        var graph = new NodeGraph
        {
            Fragments = { Frag("fA", "A", "n1"), Frag("fB", "B", "n2") }
        };

        var (sorted, error) = GraphTopologyHelper.TopologicalSortFragments(graph);
        Assert.That(error, Is.Null);
        Assert.That(sorted, Is.EquivalentTo(new[] { "fA", "fB" }));
    }

    // ================================================================
    // ExecuteFragment テスト
    // ================================================================

    [Test]
    public void ExecuteFragment_NotFound_ReturnsError()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);
        var graph = new NodeGraph { Fragments = { Frag("fA", "A", "n1") } };
        var store = new InMemorySnapshotStore();

        var result = executor.ExecuteFragment(graph, "nonexistent", BaseCtx(), store);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("not found"));
    }

    [Test]
    public void ExecuteFragment_OnlyExecutesNodesInFragment()
    {
        // Fragment A: const-string ノード "n-const" → snapshot-any "n-snap"
        // Fragment B: log ノード "n-log" (Fragment A の実行では動かないはず)
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        var executedNodes = new List<string>();
        executor.OnNodeProgress = (id, status, _) =>
        {
            if (status == "done") executedNodes.Add(id);
        };

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-const", "ngol.logic.const_string"),
                Node("n-snap", "ngol.snapshot.any"),
                Node("n-log", "ngol.logic.log")
            },
            Connections =
            {
                Conn("n-const", "value", "n-snap", "value")
            },
            Fragments =
            {
                Frag("fA", "A", "n-const", "n-snap"),
                Frag("fB", "B", "n-log")
            }
        };

        var store = new InMemorySnapshotStore();
        var result = executor.ExecuteFragment(graph, "fA", BaseCtx(), store);

        Assert.That(result.Success, Is.True);
        Assert.That(executedNodes, Does.Contain("n-const"));
        Assert.That(executedNodes, Does.Contain("n-snap"));
        Assert.That(executedNodes, Does.Not.Contain("n-log"));
    }

    [Test]
    public void ExecuteFragment_SnapshotNode_StoresValueInStore()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-const", "ngol.logic.const_string"),
                Node("n-snap", "ngol.snapshot.any")
            },
            Connections = { Conn("n-const", "value", "n-snap", "value") },
            Fragments = { Frag("fA", "A", "n-const", "n-snap") }
        };

        // const_string ノードの paramValues は動的に設定
        graph.Nodes[0].ParamValues["value"] = System.Text.Json.JsonSerializer.SerializeToElement("TestValue");

        var store = new InMemorySnapshotStore();
        var result = executor.ExecuteFragment(graph, "fA", BaseCtx(), store);

        Assert.That(result.Success, Is.True);
        Assert.That(store.HasSnapshot("n-snap", "value"), Is.True);
        Assert.That(store.GetSnapshot("n-snap", "value"), Is.EqualTo("TestValue"));
    }

    [Test]
    public void ExecuteFragment_FragmentLink_InjectsSnapshotIntoNextFragment()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-const", "ngol.logic.const_string"),
                Node("n-snap", "ngol.snapshot.any"),
                Node("n-log", "ngol.logic.log")   // log は入力 "value" を受け取る
            },
            Connections = { Conn("n-const", "value", "n-snap", "value") },
            Fragments =
            {
                Frag("fA", "A", "n-const", "n-snap"),
                Frag("fB", "B", "n-log")
            },
            FragmentLinks = { FLink("n-snap", "n-log", "value") }
        };

        graph.Nodes[0].ParamValues["value"] = System.Text.Json.JsonSerializer.SerializeToElement("Hello");

        var store = new InMemorySnapshotStore();

        // Fragment A を先に実行してスナップショットを保存
        executor.ExecuteFragment(graph, "fA", BaseCtx(), store);

        // Fragment B 実行時に FragmentLink 経由で値が注入される
        var logMessages = new List<string>();
        executor.OnNodeProgress = null;
        var result = executor.ExecuteFragment(graph, "fB", BaseCtx(), store);

        Assert.That(result.Success, Is.True);
        // ログに n-log の実行が含まれる（値注入で失敗しない）
        Assert.That(result.Logs.Any(l => l.NodeInstanceId == "n-log" && l.Level == LogLevel.Info), Is.True);
    }

    [Test]
    public void ExecuteFragment_CascadeExecution_UpstreamAutoExecuted()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        var executedFragOrder = new List<string>();
        executor.OnNodeProgress = (id, status, _) =>
        {
            if (status == "running")
            {
                if (id == "n-snap") executedFragOrder.Add("fA_snap");
                if (id == "n-log") executedFragOrder.Add("fB_log");
            }
        };

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-const", "ngol.logic.const_string"),
                Node("n-snap", "ngol.snapshot.any"),
                Node("n-log", "ngol.logic.log")
            },
            Connections = { Conn("n-const", "value", "n-snap", "value") },
            Fragments =
            {
                Frag("fA", "A", "n-const", "n-snap"),
                Frag("fB", "B", "n-log")
            },
            FragmentLinks = { FLink("n-snap", "n-log", "value") }
        };

        graph.Nodes[0].ParamValues["value"] = System.Text.Json.JsonSerializer.SerializeToElement("Cascade");

        // Fragment B だけ直接実行 → 上流 Fragment A が自動実行される
        var store = new InMemorySnapshotStore();
        var result = executor.ExecuteFragment(graph, "fB", BaseCtx(), store);

        Assert.That(result.Success, Is.True);
        // n-snap（fA）が先に実行されてから n-log（fB）が実行される
        Assert.That(executedFragOrder.IndexOf("fA_snap"), Is.LessThan(executedFragOrder.IndexOf("fB_log")));
    }

    [Test]
    public void ExecuteFragment_CascadeNotDuplicated_UpstreamExecutedOnce()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        int snapExecCount = 0;
        executor.OnNodeProgress = (id, status, _) =>
        {
            if (id == "n-snap" && status == "running") snapExecCount++;
        };

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-const", "ngol.logic.const_string"),
                Node("n-snap", "ngol.snapshot.any"),
                Node("n-log1", "ngol.logic.log"),
                Node("n-log2", "ngol.logic.log")
            },
            Connections = { Conn("n-const", "value", "n-snap", "value") },
            Fragments =
            {
                Frag("fA", "A", "n-const", "n-snap"),
                Frag("fB", "B", "n-log1", "n-log2")
            },
            FragmentLinks =
            {
                FLink("n-snap", "n-log1", "value"),
                FLink("n-snap", "n-log2", "value")
            }
        };

        graph.Nodes[0].ParamValues["value"] = System.Text.Json.JsonSerializer.SerializeToElement("X");

        var store = new InMemorySnapshotStore();
        executor.ExecuteFragment(graph, "fB", BaseCtx(), store);

        // n-snap は1回だけ実行されるべき（2つの FragmentLink があっても多重実行しない）
        Assert.That(snapExecCount, Is.EqualTo(1));
    }

    [Test]
    public void ExecuteFragment_PinnedUpstream_NotCascaded()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        int snapExecCount = 0;
        executor.OnNodeProgress = (id, status, _) =>
        {
            if (id == "n-snap" && status == "running") snapExecCount++;
        };

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-const", "ngol.logic.const_string"),
                Node("n-snap", "ngol.snapshot.any"),
                Node("n-log", "ngol.logic.log")
            },
            Connections = { Conn("n-const", "value", "n-snap", "value") },
            Fragments =
            {
                Frag("fA", "A", "n-const", "n-snap"),
                Frag("fB", "B", "n-log")
            },
            FragmentLinks = { FLink("n-snap", "n-log", "value") }
        };

        graph.Nodes[0].ParamValues["value"] = System.Text.Json.JsonSerializer.SerializeToElement("Y");

        var store = new InMemorySnapshotStore();
        var pinned = new HashSet<string> { "fA" };

        // fA がピン留めされているのでカスケードしない
        executor.ExecuteFragment(graph, "fB", BaseCtx(), store, pinned);
        Assert.That(snapExecCount, Is.EqualTo(0));
    }

    // ================================================================
    // ExecuteAllFragments テスト
    // ================================================================

    [Test]
    public void ExecuteAllFragments_EmptyFragments_FallbackToFullGraph()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        var executedNodes = new List<string>();
        executor.OnNodeProgress = (id, status, _) =>
        {
            if (status == "done") executedNodes.Add(id);
        };

        var graph = new NodeGraph
        {
            Nodes = { Node("n-const", "ngol.logic.const_string"), Node("n-log", "ngol.logic.log") },
            Connections = { Conn("n-const", "value", "n-log", "value") }
            // Fragments は空
        };

        var store = new InMemorySnapshotStore();
        var result = executor.ExecuteAllFragments(graph, BaseCtx(), store);

        Assert.That(result.Success, Is.True);
        Assert.That(executedNodes, Does.Contain("n-const"));
        Assert.That(executedNodes, Does.Contain("n-log"));
    }

    [Test]
    public void ExecuteAllFragments_SkipsPinnedFragments()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        var executedNodes = new List<string>();
        executor.OnNodeProgress = (id, status, _) =>
        {
            if (status == "running") executedNodes.Add(id);
        };

        var graph = new NodeGraph
        {
            Nodes = { Node("n1", "ngol.logic.const_string"), Node("n2", "ngol.logic.log") },
            Fragments =
            {
                Frag("fA", "A", "n1"),
                Frag("fB", "B", "n2")
            }
        };

        var store = new InMemorySnapshotStore();
        var pinned = new HashSet<string> { "fA" };

        executor.ExecuteAllFragments(graph, BaseCtx(), store, pinned);

        Assert.That(executedNodes, Does.Not.Contain("n1"));
        Assert.That(executedNodes, Does.Contain("n2"));
    }

    [Test]
    public void ExecuteAllFragments_ExecutesInTopologicalOrder()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        var execOrder = new List<string>();
        executor.OnNodeProgress = (id, status, _) =>
        {
            if (status == "running") execOrder.Add(id);
        };

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-const", "ngol.logic.const_string"),
                Node("n-snap", "ngol.snapshot.any"),
                Node("n-log", "ngol.logic.log")
            },
            Connections = { Conn("n-const", "value", "n-snap", "value") },
            Fragments =
            {
                Frag("fA", "A", "n-const", "n-snap"),
                Frag("fB", "B", "n-log")
            },
            FragmentLinks = { FLink("n-snap", "n-log", "value") }
        };

        graph.Nodes[0].ParamValues["value"] = System.Text.Json.JsonSerializer.SerializeToElement("Order");

        var store = new InMemorySnapshotStore();
        var result = executor.ExecuteAllFragments(graph, BaseCtx(), store);

        Assert.That(result.Success, Is.True);
        // n-snap (fA) は n-log (fB) より前に実行される
        Assert.That(execOrder.IndexOf("n-snap"), Is.LessThan(execOrder.IndexOf("n-log")));
    }
}
