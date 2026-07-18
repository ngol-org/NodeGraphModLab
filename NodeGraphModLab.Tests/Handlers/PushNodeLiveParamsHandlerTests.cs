using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Server;
using NodeGraphModLab.Server.Handlers;

namespace NodeGraphModLab.Tests.Handlers;

[TestFixture]
public class PushNodeLiveParamsHandlerTests
{
    private static PushNodeLiveParamsHandler MakeHandler(ILiveParamStore? store = null)
    {
        var ctx = new HandlerContext(
            new NodeRegistry(),
            store ?? new LiveParamStore(),
            new DebugLogStore(),
            log: null,
            graphSaveDir: "",
            dynamicNodesDir: "",
            nodesDir: "",
            nodePacksDir: "",
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>(),
            new System.Collections.Concurrent.ConcurrentQueue<PendingExecution>(),
            new PersistentNodeRunner(),
            () => null,
            new GraphExecutor(new NodeRegistry()));
        return new PushNodeLiveParamsHandler(ctx);
    }

    private static JsonDocument Msg(string json) => JsonDocument.Parse(json);

    [Test]
    public async Task Handle_MergesParamsAndSendsSuccessResponse()
    {
        var store = new LiveParamStore();
        var handler = MakeHandler(store);
        var session = new TestSession();
        using var doc = Msg("{\"nodeInstanceId\":\"node-1\",\"params\":{\"scale\":1.25,\"enabled\":true}}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(store.TryGet("node-1", "scale", out var scale), Is.True);
        Assert.That(scale, Is.EqualTo(1.25));
        Assert.That(store.TryGet("node-1", "enabled", out var enabled), Is.True);
        Assert.That(enabled, Is.EqualTo(true));
        Assert.That(session.SentMessages, Has.Count.EqualTo(1));

        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("type").GetString(), Is.EqualTo("push_node_live_params_response"));
        Assert.That(resp.RootElement.GetProperty("success").GetBoolean(), Is.True);
        var keys = resp.RootElement.GetProperty("mergedKeys").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.That(keys, Does.Contain("scale"));
        Assert.That(keys, Does.Contain("enabled"));
    }

    [Test]
    public async Task Handle_UnsupportedType_DoesNotMerge()
    {
        var store = new LiveParamStore();
        var handler = MakeHandler(store);
        var session = new TestSession();
        using var doc = Msg("{\"nodeInstanceId\":\"node-1\",\"params\":{\"bad\":{\"a\":1}}}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(store.TryGet("node-1", "bad", out _), Is.False);
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(resp.RootElement.GetProperty("reason").GetString(), Is.EqualTo("unsupported_type"));
    }

    [Test]
    public async Task Handle_MissingNodeInstanceId_SendsNothing()
    {
        var handler = MakeHandler();
        var session = new TestSession();
        using var doc = Msg("{\"params\":{\"scale\":1.0}}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SentMessages, Is.Empty);
    }
}
