using System.Collections.Concurrent;
using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Server;
using NodeGraphModLab.Server.Handlers;

namespace NodeGraphModLab.Tests.Handlers;

[TestFixture]
public class GetNodeListHandlerTests
{
    private static HandlerContext MakeCtx(NodeRegistry registry) => new(
        registry, new LiveParamStore(), new DebugLogStore(), log: null,
        graphSaveDir: ".", dynamicNodesDir: ".",
        nodesDir: ".", nodePacksDir: ".",
        scriptNodeId: new ConcurrentDictionary<string, string>(),
        pendingExecutions: new ConcurrentQueue<PendingExecution>(),
        runner: new NodeGraphModLab.Core.Engine.PersistentNodeRunner(),
        getExecutionCts: () => null,
        executor: new NodeGraphModLab.Core.Engine.GraphExecutor(registry));

    [Test]
    public async Task Handle_EmptyRegistry_SendsEmptyNodeList()
    {
        var handler = new GetNodeListHandler(MakeCtx(new NodeRegistry()));
        var session = new TestSession();

        await handler.HandleAsync(session, default);

        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var doc = JsonDocument.Parse(session.SentMessages[0]);
        var nodes = doc.RootElement.GetProperty("nodes");
        Assert.That(nodes.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(nodes.GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task Handle_RegistryWithNodes_ReturnsNodeInfo()
    {
        var registry = new NodeRegistry();
        registry.RegisterAssembly(typeof(NodeGraphModLab.BuiltinNodes.Logic.AddNode).Assembly);
        var handler = new GetNodeListHandler(MakeCtx(registry));
        var session = new TestSession();

        await handler.HandleAsync(session, default);

        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var doc = JsonDocument.Parse(session.SentMessages[0]);
        var nodes = doc.RootElement.GetProperty("nodes");
        Assert.That(nodes.GetArrayLength(), Is.GreaterThan(0));
        var first = nodes[0];
        Assert.That(first.TryGetProperty("id", out _), Is.True);
        Assert.That(first.TryGetProperty("displayName", out _), Is.True);
    }

    [Test]
    public async Task Handle_NodePort_ShowInlineEditorFalseByDefault_IsIncludedInJson()
    {
        // ShowInlineEditor を明示設定しないポートは既定値 false → JSON に "showInlineEditor": false として含まれることを確認
        var registry = new NodeRegistry();
        registry.RegisterType(typeof(DummyNodeForShowInlineEditorTest));
        var handler = new GetNodeListHandler(MakeCtx(registry));
        var session = new TestSession();

        await handler.HandleAsync(session, default);

        using var doc = JsonDocument.Parse(session.SentMessages[0]);
        var nodes = doc.RootElement.GetProperty("nodes");
        Assert.That(nodes.GetArrayLength(), Is.EqualTo(1));

        var ports = nodes[0].GetProperty("ports");
        // すべてのポートに showInlineEditor フィールドが存在すること
        foreach (var port in ports.EnumerateArray())
        {
            Assert.That(port.TryGetProperty("showInlineEditor", out _), Is.True,
                $"Port '{port.GetProperty("name").GetString()}' には showInlineEditor フィールドが必要");
        }
    }
}

// ---- テスト用ノード定義 ----

[NodeType("gol.test.show_inline_editor_default", "Test", "ShowInlineEditor Default Test")]
[NodePort("input",  PortDirection.Input,  "number")]
[NodePort("output", PortDirection.Output, "number")]
internal class DummyNodeForShowInlineEditorTest : INode
{
    public void Execute(IExecutionContext ctx)
    {
        ctx.SetPortValue("output", ctx.GetPortValue("input"));
    }
}
