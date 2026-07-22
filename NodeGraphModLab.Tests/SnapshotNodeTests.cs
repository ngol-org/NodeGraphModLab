using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.BuiltinNodes.Fragment;
using NUnit.Framework;

namespace NodeGraphModLab.Tests;

/// <summary>
/// Phase 3: SnapshotNode 定義テスト。
/// - NodeType 属性の存在確認
/// - Execute 実行で SnapshotStore に値が保存されること
/// - 出力ポートにも値が流れること
/// - SnapshotListNode の IEnumerable materialize 確認
/// </summary>
[TestFixture]
public class SnapshotNodeTests
{
    private static readonly (Type type, string expectedId, string portName, string expectedCategory)[] NodeTypes =
    {
        (typeof(SnapshotAnyNode),        "ngol.snapshot.any",        "value",      "Fragment/Snapshot"),
        (typeof(SnapshotNumberNode),     "ngol.snapshot.number",     "number",     "Fragment/Snapshot"),
        (typeof(SnapshotStringNode),     "ngol.snapshot.string",     "string",     "Fragment/Snapshot"),
        (typeof(SnapshotBoolNode),       "ngol.snapshot.bool",       "bool",       "Fragment/Snapshot"),
        (typeof(SnapshotGameObjectNode), "ngol.snapshot.gameobject", "gameobject", "Fragment/Snapshot"),
        (typeof(SnapshotMaterialNode),   "ngol.snapshot.material",   "material",   "Fragment/Snapshot"),
        (typeof(SnapshotTransformNode),  "ngol.snapshot.transform",  "transform",  "Fragment/Snapshot"),
        (typeof(SnapshotComponentNode),  "ngol.snapshot.component",  "component",  "Fragment/Snapshot"),
        (typeof(SnapshotRigidbodyNode),  "ngol.snapshot.rigidbody",  "rigidbody",  "Fragment/Snapshot"),
        (typeof(SnapshotColliderNode),   "ngol.snapshot.collider",   "collider",   "Fragment/Snapshot"),
        (typeof(SnapshotVector2Node),    "ngol.snapshot.vector2",    "vector2",    "Fragment/Snapshot"),
        (typeof(SnapshotVector3Node),    "ngol.snapshot.vector3",    "vector3",    "Fragment/Snapshot"),
        (typeof(SnapshotVector4Node),    "ngol.snapshot.vector4",    "vector4",    "Fragment/Snapshot"),
        (typeof(SnapshotColorNode),      "ngol.snapshot.color",      "color",      "Fragment/Snapshot"),
        (typeof(SnapshotQuaternionNode), "ngol.snapshot.quaternion", "quaternion", "Fragment/Snapshot"),
        (typeof(SnapshotRectNode),       "ngol.snapshot.rect",       "rect",       "Fragment/Snapshot"),
        (typeof(SnapshotBoundsNode),     "ngol.snapshot.bounds",     "bounds",     "Fragment/Snapshot"),
        (typeof(SnapshotListNode),       "ngol.snapshot.list",       "list",       "Fragment/Snapshot"),
    };

    [Test]
    [TestCaseSource(nameof(NodeTypes))]
    public void SnapshotNode_HasCorrectNodeTypeId((Type type, string expectedId, string portName, string expectedCategory) tc)
    {
        var attr = tc.type.GetCustomAttribute<NodeTypeAttribute>();
        Assert.That(attr, Is.Not.Null, $"{tc.type.Name} missing [NodeType]");
        Assert.That(attr!.Id, Is.EqualTo(tc.expectedId));
        Assert.That(attr.Category, Is.EqualTo(tc.expectedCategory));
    }

    [Test]
    [TestCaseSource(nameof(NodeTypes))]
    public void SnapshotNode_HasTypedInputAndOutputPorts((Type type, string expectedId, string portName, string expectedCategory) tc)
    {
        var ports = tc.type.GetCustomAttributes<NodePortAttribute>().ToList();
        Assert.That(ports.Any(p => p.Name == tc.portName && p.Direction == PortDirection.Input),
            Is.True, $"{tc.type.Name}: missing input port '{tc.portName}'");
        Assert.That(ports.Any(p => p.Name == tc.portName && p.Direction == PortDirection.Output),
            Is.True, $"{tc.type.Name}: missing output port '{tc.portName}'");
    }

    [Test]
    [TestCaseSource(nameof(NodeTypes))]
    public void SnapshotNode_CanBeInstantiated((Type type, string expectedId, string portName, string expectedCategory) tc)
    {
        var node = Activator.CreateInstance(tc.type) as INode;
        Assert.That(node, Is.Not.Null);
    }

    [Test]
    public void SnapshotAnyNode_StoresValueInSnapshotStore()
    {
        var store = new InMemorySnapshotStore();
        var node = new SnapshotAnyNode();
        var ctx = new TestExecutionContext("snap-001", store)
            .WithInput("value", "hello");

        node.Execute(ctx);

        Assert.That(store.HasSnapshot("snap-001", "value"), Is.True);
        Assert.That(store.GetSnapshot("snap-001", "value"), Is.EqualTo("hello"));
        Assert.That(ctx.GetOutput("value"), Is.EqualTo("hello"));
    }

    [Test]
    public void SnapshotAnyNode_NullInput_DefaultAllowNull_DoesNotStore()
    {
        // T12.1: allowNull=false (デフォルト) では null 入力時は Snapshot に保存しない
        var store = new InMemorySnapshotStore();
        var node = new SnapshotAnyNode();
        var ctx = new TestExecutionContext("snap-002", store)
            .WithInput("value", null);

        node.Execute(ctx);

        Assert.That(store.HasSnapshot("snap-002", "value"), Is.False);
    }

    [Test]
    public void SnapshotAnyNode_NullInput_AllowNullTrue_StoresNull()
    {
        // T12.1: allowNull=true を指定した場合は null 入力でも Snapshot に保存する
        var store = new InMemorySnapshotStore();
        var node = new SnapshotAnyNode();
        var ctx = new TestExecutionContext("snap-002b", store)
            .WithInput("value", null)
            .WithParam("allowNull", true);

        node.Execute(ctx);

        Assert.That(store.HasSnapshot("snap-002b", "value"), Is.True);
        Assert.That(store.GetSnapshot("snap-002b", "value"), Is.Null);
    }

    [Test]
    public void SnapshotAnyNode_NoSnapshotStore_DoesNotThrow()
    {
        var node = new SnapshotAnyNode();
        var ctx = new TestExecutionContext("snap-003", snapshotStore: null)
            .WithInput("value", 42.0);

        Assert.DoesNotThrow(() => node.Execute(ctx));
        Assert.That(ctx.GetOutput("value"), Is.EqualTo(42.0));
    }

    [Test]
    public void SnapshotListNode_IEnumerable_IsMaterialized()
    {
        var store = new InMemorySnapshotStore();
        var node = new SnapshotListNode();

        // IEnumerable<object?> (遅延列挙に見立てる)
        IEnumerable<object?> LazyEnum() { yield return 1.0; yield return 2.0; yield return 3.0; }

        var ctx = new TestExecutionContext("snap-list-001", store)
            .WithInput("list", (object)LazyEnum());

        node.Execute(ctx);

        var saved = store.GetSnapshot("snap-list-001", "list");
        Assert.That(saved, Is.InstanceOf<List<object?>>());
        var list = (List<object?>)saved!;
        Assert.That(list, Has.Count.EqualTo(3));
        Assert.That(list[0], Is.EqualTo(1.0));
    }

    [Test]
    public void SnapshotListNode_AlreadyList_WrapsNotDoubled()
    {
        var store = new InMemorySnapshotStore();
        var node = new SnapshotListNode();
        var input = new List<object?> { "a", "b" };

        var ctx = new TestExecutionContext("snap-list-002", store)
            .WithInput("list", (object)input);

        node.Execute(ctx);

        var saved = store.GetSnapshot("snap-list-002", "list");
        Assert.That(saved, Is.InstanceOf<List<object?>>());
        Assert.That(((List<object?>)saved!), Has.Count.EqualTo(2));
    }

    [Test]
    public void AllSnapshotNodes_Count_Is18()
    {
        Assert.That(NodeTypes, Has.Length.EqualTo(18));
    }
}

// ---- テスト用コンテキスト ----

internal sealed class TestExecutionContext : IExecutionContext
{
    private readonly Dictionary<string, object?> _inputs = new();
    private readonly Dictionary<string, object?> _outputs = new();
    private readonly Dictionary<string, object?> _params = new();

    public string NodeInstanceId { get; }
    public ISnapshotStore? SnapshotStore { get; }
    public IKVStore Store { get; } = new NullKVStore();
    public INodeLogger Logger { get; } = new NullNodeLogger();

    public TestExecutionContext(string instanceId, ISnapshotStore? snapshotStore = null)
    {
        NodeInstanceId = instanceId;
        SnapshotStore = snapshotStore;
    }

    public TestExecutionContext WithInput(string portName, object? value)
    {
        _inputs[portName] = value;
        return this;
    }

    public TestExecutionContext WithParam(string paramName, object? value)
    {
        _params[paramName] = value;
        return this;
    }

    public object? GetOutput(string portName) => _outputs.TryGetValue(portName, out var v) ? v : null;

    public object? GetPortValue(string portName) => _inputs.TryGetValue(portName, out var v) ? v : null;
    public void SetPortValue(string portName, object? value) => _outputs[portName] = value;
    public T? GetParam<T>(string paramName)
    {
        if (_params.TryGetValue(paramName, out var v) && v is T typed) return typed;
        return default;
    }
    public T GetLiveParam<T>(string key, T defaultValue = default!) => defaultValue;
    public void MainThreadDispatch(Action action) => action();
    public IReadOnlyList<PortConnection> GetDownstreamConnections(string outputPortName)
        => Array.Empty<PortConnection>();
    public void PushLiveValue(string portName, object? value) { }
    public IReadOnlyList<NodeQuickLaunchInfo> GetNodesByInputPortType(string portType)
        => Array.Empty<NodeQuickLaunchInfo>();
    public void QuickExecuteNode(string nodeTypeId, string inputPortName, object? inputValue) { }
    public IPersistentRegistration RegisterPersistent(PersistentCallbacks callbacks)
        => new NullRegistration();
    public T? GetExtensionService<T>() where T : class => null;
}

internal sealed class NullNodeLogger : INodeLogger
{
    public void LogInfo(string message) { }
    public void LogWarning(string message) { }
    public void LogError(string message) { }
    public void LogDebug(string message) { }
}

internal sealed class NullRegistration : IPersistentRegistration
{
    public bool IsActive => false;
    public void Cancel() { }
    public void ReportProgress(string message) { }
    public void Dispose() { }
}

internal sealed class NullKVStore : IKVStore
{
    public void Set(string key, object? value) { }
    public object? Get(string key) => null;
    public T? Get<T>(string key) => default;
    public bool TryGet<T>(string key, out T? value) { value = default; return false; }
    public bool ContainsKey(string key) => false;
    public void Delete(string key) { }
    public IEnumerable<string> Keys(string? prefix = null) => Array.Empty<string>();
}
