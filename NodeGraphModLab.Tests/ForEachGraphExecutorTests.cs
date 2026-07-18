using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.BuiltinNodes.Logic;

namespace NodeGraphModLab.Tests;

/// <summary>
/// GraphExecutor の ForEach ループ展開ロジックの統合テスト。
/// current_item/index に依存しない後続ノード（例: count consumer）はループ後に1回だけ
/// 実行されること、items が paramValues のリテラル配列でも解決されることを検証する。
/// </summary>
[TestFixture]
public class ForEachGraphExecutorTests
{
    /// <summary>テスト専用: 固定リストを items として出力するダミーノード。</summary>
    [NodeType("test.foreach.list_source", "Test", "List Source")]
    [NodePort("items", PortDirection.Output, "any[]")]
    private sealed class ListSourceNode : INode
    {
        public static List<object?> Items = new();
        public void Execute(IExecutionContext ctx) => ctx.SetPortValue("items", Items);
    }

    /// <summary>テスト専用: 入力ポートを持たず固定値を出力するだけの独立ノード（Constant相当）。</summary>
    [NodeType("test.foreach.constant", "Test", "Constant")]
    [NodePort("value", PortDirection.Output, "double")]
    private sealed class ConstantNode : INode
    {
        public static double Value;
        public void Execute(IExecutionContext ctx) => ctx.SetPortValue("value", Value);
    }

    /// <summary>テスト専用: a+b を計算しつつ、呼び出しごとの (a, b) 組を記録するノード（ループ本体役）。</summary>
    [NodeType("test.foreach.recording_add", "Test", "Recording Add")]
    [NodePort("a", PortDirection.Input, "double")]
    [NodePort("b", PortDirection.Input, "double")]
    [NodePort("result", PortDirection.Output, "double")]
    private sealed class RecordingAddNode : INode
    {
        public static List<(double a, double b)> Calls = new();
        public void Execute(IExecutionContext ctx)
        {
            var a = Convert.ToDouble(ctx.GetPortValue("a") ?? 0.0);
            var b = Convert.ToDouble(ctx.GetPortValue("b") ?? 0.0);
            Calls.Add((a, b));
            ctx.SetPortValue("result", a + b);
        }
    }

    // ---- ヘルパー ----

    private static NodeRegistry BuildRegistry()
    {
        var registry = new NodeRegistry();
        registry.RegisterAssembly(typeof(ForEachNode).Assembly);
        registry.RegisterAssembly(typeof(ListSourceNode).Assembly);
        return registry;
    }

    private static TestExecutionContext BaseCtx() => new TestExecutionContext("base");

    private static NodeInstance Node(string instanceId, string nodeTypeId, Dictionary<string, JsonElement>? paramValues = null)
        => new NodeInstance { InstanceId = instanceId, NodeTypeId = nodeTypeId, ParamValues = paramValues ?? new() };

    private static NodeConnection Conn(string fromId, string fromPort, string toId, string toPort)
        => new NodeConnection { FromNodeInstanceId = fromId, FromPortName = fromPort, ToNodeInstanceId = toId, ToPortName = toPort };

    private static FragmentDefinition Frag(string id, string name, params string[] nodeIds)
        => new FragmentDefinition { Id = id, Name = name, NodeInstanceIds = nodeIds.ToList() };

    private static Dictionary<string, int> TrackExecutionCounts(GraphExecutor executor)
    {
        var counts = new Dictionary<string, int>();
        executor.OnNodeProgress = (id, status, _) =>
        {
            if (status != "done") return;
            counts[id] = counts.TryGetValue(id, out var c) ? c + 1 : 1;
        };
        return counts;
    }

    // ================================================================
    // バグ①: ループ本体判定（current_item/index の推移閉包）
    // ================================================================

    [Test]
    public void ForEach_CountConsumer_RunsOnceAfterLoop()
    {
        ListSourceNode.Items = new List<object?> { "a", "b", "c" };

        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);
        var counts = TrackExecutionCounts(executor);

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-source", "test.foreach.list_source"),
                Node("n-foreach", "ngol.logic.foreach"),
                Node("n-count-log", "ngol.logic.log"),
            },
            Connections =
            {
                Conn("n-source", "items", "n-foreach", "items"),
                Conn("n-foreach", "count", "n-count-log", "value"),
            }
        };

        var result = executor.Execute(graph, BaseCtx());

        Assert.That(result.Success, Is.True);
        Assert.That(counts["n-count-log"], Is.EqualTo(1),
            "count にしか依存しないノードはループ後に1回だけ実行されるべき");
    }

    [Test]
    public void ForEach_CurrentItemConsumer_RunsOncePerItem()
    {
        ListSourceNode.Items = new List<object?> { "a", "b", "c" };

        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);
        var counts = TrackExecutionCounts(executor);

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-source", "test.foreach.list_source"),
                Node("n-foreach", "ngol.logic.foreach"),
                Node("n-item-log", "ngol.logic.log"),
            },
            Connections =
            {
                Conn("n-source", "items", "n-foreach", "items"),
                Conn("n-foreach", "current_item", "n-item-log", "value"),
            }
        };

        var result = executor.Execute(graph, BaseCtx());

        Assert.That(result.Success, Is.True);
        Assert.That(counts["n-item-log"], Is.EqualTo(3),
            "current_item に依存するノードは引き続きアイテム数分実行されるべき（既存挙動の保持）");
    }

    [Test]
    public void ForEach_EmptyList_PostLoopNodeStillRunsOnce()
    {
        ListSourceNode.Items = new List<object?>();

        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);
        var counts = TrackExecutionCounts(executor);

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-source", "test.foreach.list_source"),
                Node("n-foreach", "ngol.logic.foreach"),
                Node("n-count-log", "ngol.logic.log"),
            },
            Connections =
            {
                Conn("n-source", "items", "n-foreach", "items"),
                Conn("n-foreach", "count", "n-count-log", "value"),
            }
        };

        var result = executor.Execute(graph, BaseCtx());

        Assert.That(result.Success, Is.True);
        Assert.That(counts.TryGetValue("n-count-log", out var c) ? c : 0, Is.EqualTo(1),
            "items が0件でも、ループ非依存ノードは1回実行されるべき");
    }

    // ================================================================
    // バグ②: items の paramValues リテラル配列フォールバック
    // ================================================================

    [Test]
    public void ForEach_ItemsFromParamValues_LiteralArray_Iterates()
    {
        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);
        var counts = TrackExecutionCounts(executor);

        var itemsJson = JsonDocument.Parse("[\"Alpha\",\"Beta\",\"Gamma\"]").RootElement;

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-foreach", "ngol.logic.foreach", new Dictionary<string, JsonElement> { ["items"] = itemsJson }),
                Node("n-item-log", "ngol.logic.log"),
            },
            Connections =
            {
                Conn("n-foreach", "current_item", "n-item-log", "value"),
            }
        };

        var result = executor.Execute(graph, BaseCtx());

        Assert.That(result.Success, Is.True);
        Assert.That(counts["n-item-log"], Is.EqualTo(3),
            "items が未接続でも paramValues のリテラル配列で正しくループするべき");
    }

    // ================================================================
    // Fragment 実行経路（ExecuteFragment）でも ForEach がループ展開されること
    // ================================================================

    [Test]
    public void ExecuteFragment_CurrentItemConsumer_RunsOncePerItem()
    {
        // WebUI のノード単体 Run ボタンは実際には「そのノードが属する Fragment を実行する」
        // ExecuteFragment 経路を通る。この経路にも ForEach ループ展開が効くことを検証する。
        ListSourceNode.Items = new List<object?> { "a", "b", "c" };

        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);
        var counts = TrackExecutionCounts(executor);

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-source", "test.foreach.list_source"),
                Node("n-foreach", "ngol.logic.foreach"),
                Node("n-item-log", "ngol.logic.log"),
            },
            Connections =
            {
                Conn("n-source", "items", "n-foreach", "items"),
                Conn("n-foreach", "current_item", "n-item-log", "value"),
            },
            Fragments =
            {
                Frag("f1", "F1", "n-source", "n-foreach", "n-item-log"),
            }
        };

        var store = new InMemorySnapshotStore();
        var result = executor.ExecuteFragment(graph, "f1", BaseCtx(), store);

        Assert.That(result.Success, Is.True);
        Assert.That(counts["n-item-log"], Is.EqualTo(3),
            "ExecuteFragment（ノード単体Run経路）でも current_item 依存ノードはアイテム数分実行されるべき");
    }

    [Test]
    public void ExecuteFragment_CountConsumer_RunsOnceAfterLoop()
    {
        ListSourceNode.Items = new List<object?> { "a", "b", "c" };

        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);
        var counts = TrackExecutionCounts(executor);

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-source", "test.foreach.list_source"),
                Node("n-foreach", "ngol.logic.foreach"),
                Node("n-count-log", "ngol.logic.log"),
            },
            Connections =
            {
                Conn("n-source", "items", "n-foreach", "items"),
                Conn("n-foreach", "count", "n-count-log", "value"),
            },
            Fragments =
            {
                Frag("f1", "F1", "n-source", "n-foreach", "n-count-log"),
            }
        };

        var store = new InMemorySnapshotStore();
        var result = executor.ExecuteFragment(graph, "f1", BaseCtx(), store);

        Assert.That(result.Success, Is.True);
        Assert.That(counts["n-count-log"], Is.EqualTo(1),
            "ExecuteFragment 経路でも count 消費ノードはループ後に1回だけ実行されるべき");
    }

    // ================================================================
    // current_item/index に依存しない独立ノードがループ本体へ
    // 通常 connections で接続されている場合、ループ開始前に1回だけ
    // 実行されて値が正しく供給されるべき（ノード宣言順を ForEach → Constant
    // にすることで、修正前のトポロジカルソートが Constant を candidateIds
    // 側に落とす再現条件を再現している）。
    // ================================================================

    [Test]
    public void ForEach_IndependentConstantFeedingLoopBody_RunsBeforeLoopWithCorrectValue()
    {
        RecordingAddNode.Calls = new List<(double, double)>();
        ConstantNode.Value = 100;

        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        var itemsJson = JsonDocument.Parse("[1,2,3]").RootElement;

        var graph = new NodeGraph
        {
            Nodes =
            {
                // ForEach を Constant より先に宣言する（修正前バグの再現条件）
                Node("n-foreach", "ngol.logic.foreach", new Dictionary<string, JsonElement> { ["items"] = itemsJson }),
                Node("n-constant", "test.foreach.constant"),
                Node("n-add", "test.foreach.recording_add"),
            },
            Connections =
            {
                Conn("n-foreach", "current_item", "n-add", "a"),
                Conn("n-constant", "value", "n-add", "b"),
            }
        };

        var result = executor.Execute(graph, BaseCtx());

        Assert.That(result.Success, Is.True);
        Assert.That(RecordingAddNode.Calls, Is.EqualTo(new List<(double, double)> { (1, 100), (2, 100), (3, 100) }),
            "ループに依存しない独立ノード（Constant）の値がループ開始前に確定し、全イテレーションで正しく供給されるべき");
    }

    [Test]
    public void ExecuteFragment_IndependentConstantFeedingLoopBody_RunsBeforeLoopWithCorrectValue()
    {
        RecordingAddNode.Calls = new List<(double, double)>();
        ConstantNode.Value = 100;

        var registry = BuildRegistry();
        var executor = new GraphExecutor(registry);

        var itemsJson = JsonDocument.Parse("[1,2,3]").RootElement;

        var graph = new NodeGraph
        {
            Nodes =
            {
                Node("n-foreach", "ngol.logic.foreach", new Dictionary<string, JsonElement> { ["items"] = itemsJson }),
                Node("n-constant", "test.foreach.constant"),
                Node("n-add", "test.foreach.recording_add"),
            },
            Connections =
            {
                Conn("n-foreach", "current_item", "n-add", "a"),
                Conn("n-constant", "value", "n-add", "b"),
            },
            Fragments =
            {
                Frag("f1", "F1", "n-foreach", "n-constant", "n-add"),
            }
        };

        var store = new InMemorySnapshotStore();
        var result = executor.ExecuteFragment(graph, "f1", BaseCtx(), store);

        Assert.That(result.Success, Is.True);
        Assert.That(RecordingAddNode.Calls, Is.EqualTo(new List<(double, double)> { (1, 100), (2, 100), (3, 100) }),
            "ExecuteFragment 経路（WebUI ノード単体 Run 相当）でも同様に正しく供給されるべき");
    }
}
