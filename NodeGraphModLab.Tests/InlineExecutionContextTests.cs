using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Tests;

/// <summary>
/// 入力ポートがインスペクター/ノードのインライン欄で直接入力された値
/// （paramValues のみで、配線=connectionsが無い場合）が GetPortValue から
/// 読めることを検証する。
/// </summary>
[TestFixture]
public class InlineExecutionContextTests
{
    private static InlineExecutionContext MakeCtx(
        Dictionary<string, JsonElement>? paramValues = null,
        Dictionary<string, object?>? inputValues = null)
    {
        return new InlineExecutionContext(
            "node-1",
            paramValues ?? new Dictionary<string, JsonElement>(),
            inputValues ?? new Dictionary<string, object?>(),
            new StubParentContext());
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Test]
    public void GetPortValue_Unconnected_FallsBackToParamValues_String()
    {
        var ctx = MakeCtx(paramValues: new() { ["memberName"] = Parse("\"velocity\"") });

        Assert.That(ctx.GetPortValue("memberName"), Is.EqualTo("velocity"));
    }

    [Test]
    public void GetPortValue_Unconnected_FallsBackToParamValues_Number()
    {
        var ctx = MakeCtx(paramValues: new() { ["a"] = Parse("5") });

        Assert.That(ctx.GetPortValue("a"), Is.EqualTo(5.0));
    }

    [Test]
    public void GetPortValue_Unconnected_FallsBackToParamValues_Bool()
    {
        var ctx = MakeCtx(paramValues: new() { ["condition"] = Parse("true") });

        Assert.That(ctx.GetPortValue("condition"), Is.EqualTo(true));
    }

    [Test]
    public void GetPortValue_Connected_PrefersConnectionOverParamValues()
    {
        // 配線あり（inputValues）と paramValues の両方が存在する場合は配線を優先する
        var ctx = MakeCtx(
            paramValues: new() { ["a"] = Parse("999") },
            inputValues: new() { ["a"] = 5.0 });

        Assert.That(ctx.GetPortValue("a"), Is.EqualTo(5.0));
    }

    [Test]
    public void GetPortValue_NoConnectionNoParamValue_ReturnsNull()
    {
        var ctx = MakeCtx();

        Assert.That(ctx.GetPortValue("a"), Is.Null);
    }

    [Test]
    public void GetPortValue_Unconnected_FallsBackToParamValues_Array()
    {
        var ctx = MakeCtx(paramValues: new() { ["items"] = Parse("[\"a\",\"b\",1]") });

        var result = ctx.GetPortValue("items") as List<object?>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result![0], Is.EqualTo("a"));
        Assert.That(result[1], Is.EqualTo("b"));
        Assert.That(result[2], Is.EqualTo(1.0));
    }

    [Test]
    public void GetPortValue_StructuredParamValue_ReturnsNull_UseGetParamInstead()
    {
        // object/array は GetPortValue では変換しない（ctx.GetParam<T> を使う想定）
        var ctx = MakeCtx(paramValues: new() { ["color"] = Parse("{\"r\":1,\"g\":0,\"b\":0,\"a\":1}") });

        Assert.That(ctx.GetPortValue("color"), Is.Null);
        Assert.That(ctx.GetParam<Dictionary<string, double>>("color")!["r"], Is.EqualTo(1.0));
    }

    private sealed class StubParentContext : IExecutionContext
    {
        public INodeLogger Logger { get; } = new StubLogger();
        public void MainThreadDispatch(Action action) => action();
        public object? GetPortValue(string portName) => null;
        public void SetPortValue(string portName, object? value) { }
        public string NodeInstanceId => "parent";
        public T? GetParam<T>(string paramName) => default;
        public T GetLiveParam<T>(string key, T defaultValue = default!) => defaultValue;
        public IPersistentRegistration RegisterPersistent(PersistentCallbacks callbacks)
            => throw new NotSupportedException();
        public ISnapshotStore? SnapshotStore => null;
        public IReadOnlyList<PortConnection> GetDownstreamConnections(string outputPortName)
            => Array.Empty<PortConnection>();
        public void PushLiveValue(string portName, object? value) { }
        public IReadOnlyList<NodeQuickLaunchInfo> GetNodesByInputPortType(string portType)
            => Array.Empty<NodeQuickLaunchInfo>();
        public void QuickExecuteNode(string nodeTypeId, string inputPortName, object? inputValue) { }
        public IKVStore Store => throw new NotSupportedException();
        public T? GetExtensionService<T>() where T : class => null;
    }

    private sealed class StubLogger : INodeLogger
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
        public void LogDebug(string message) { }
    }
}
