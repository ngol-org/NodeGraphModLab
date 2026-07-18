using System.Collections.Concurrent;
using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Server;
using NodeGraphModLab.Server.Handlers;

namespace NodeGraphModLab.Tests.Handlers;

[TestFixture]
public class ExecuteGraphHandlerTests
{
    private static (HandlerContext ctx, ConcurrentQueue<PendingExecution> queue) MakeCtx()
    {
        var queue = new ConcurrentQueue<PendingExecution>();
        var ctx = new HandlerContext(
            new NodeRegistry(), new LiveParamStore(), new DebugLogStore(), log: null,
            graphSaveDir: ".", dynamicNodesDir: ".",
            nodesDir: ".", nodePacksDir: ".",
            scriptNodeId: new ConcurrentDictionary<string, string>(),
            pendingExecutions: queue,
            runner: new PersistentNodeRunner(),
            getExecutionCts: () => null,
            executor: new GraphExecutor(new NodeRegistry()));
        return (ctx, queue);
    }

    [Test]
    public async Task Handle_ValidGraph_EnqueuesPendingExecution()
    {
        var (ctx, queue) = MakeCtx();
        var handler = new ExecuteGraphHandler(ctx);
        var session = new TestSession();

        var graph = new NodeGraph { Id = "test-id", Name = "Test" };
        var json = graph.ToJson();
        using var doc = JsonDocument.Parse($"{{\"graph\":{json}}}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(queue.Count, Is.EqualTo(1));
        Assert.That(session.SentMessages, Is.Empty);
    }

    [Test]
    public async Task Handle_NullGraph_SendsErrorResponse()
    {
        var (ctx, queue) = MakeCtx();
        var handler = new ExecuteGraphHandler(ctx);
        var session = new TestSession();

        // graph プロパティが null → FromJson("null") は null を返す → エラーレスポンス送信
        using var doc = JsonDocument.Parse("{\"graph\":null}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(queue.Count, Is.EqualTo(0));
        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var respDoc = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(respDoc.RootElement.TryGetProperty("message", out _), Is.True);
    }
}
