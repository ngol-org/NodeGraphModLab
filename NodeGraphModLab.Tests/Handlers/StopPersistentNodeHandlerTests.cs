using System.Collections.Concurrent;
using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Server;
using NodeGraphModLab.Server.Handlers;

namespace NodeGraphModLab.Tests.Handlers;

[TestFixture]
public class StopPersistentNodeHandlerTests
{
    private static HandlerContext MakeCtx(PersistentNodeRunner runner) => new(
        new NodeRegistry(), new LiveParamStore(), new DebugLogStore(), log: null,
        graphSaveDir: ".", dynamicNodesDir: ".",
        nodesDir: ".", nodePacksDir: ".",
        scriptNodeId: new ConcurrentDictionary<string, string>(),
        pendingExecutions: new ConcurrentQueue<PendingExecution>(),
        runner: runner,
        getExecutionCts: () => null,
        executor: new GraphExecutor(new NodeRegistry()));

    [Test]
    public async Task Handle_WithNodeInstanceId_CallsCancelByNodeId()
    {
        var runner = new PersistentNodeRunner();
        var handler = new StopPersistentNodeHandler(MakeCtx(runner));
        var session = new TestSession();
        using var doc = JsonDocument.Parse("{\"nodeInstanceId\":\"node-abc\"}");

        await handler.HandleAsync(session, doc.RootElement);

        // CancelByNodeId は該当IDが存在しなくても例外を投げず、レスポンスを返すこと
        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("found").GetBoolean(), Is.False);
    }

    [Test]
    public async Task Handle_EmptyNodeInstanceId_ReturnsFoundFalse()
    {
        var runner = new PersistentNodeRunner();
        var handler = new StopPersistentNodeHandler(MakeCtx(runner));
        var session = new TestSession();
        using var doc = JsonDocument.Parse("{\"nodeInstanceId\":\"\"}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("found").GetBoolean(), Is.False);
    }

    [Test]
    public async Task Handle_MissingNodeInstanceId_ReturnsFoundFalse()
    {
        var runner = new PersistentNodeRunner();
        var handler = new StopPersistentNodeHandler(MakeCtx(runner));
        var session = new TestSession();
        using var doc = JsonDocument.Parse("{}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("found").GetBoolean(), Is.False);
    }
}
