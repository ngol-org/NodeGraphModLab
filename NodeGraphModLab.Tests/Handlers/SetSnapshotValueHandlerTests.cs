using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Server;
using NodeGraphModLab.Server.Handlers;

namespace NodeGraphModLab.Tests.Handlers;

[TestFixture]
public class SetSnapshotValueHandlerTests
{
    private static SetSnapshotValueHandler MakeHandler() => new();

    private static JsonDocument Msg(string json) => JsonDocument.Parse(json);

    [Test]
    public async Task Handle_StringValue_SetsSnapshotAndSendsResponseAndPush()
    {
        var handler = MakeHandler();
        var session = new TestSession();
        using var doc = Msg("{\"nodeInstanceId\":\"node-1\",\"portName\":\"selected\",\"value\":\"Charlie\"}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SnapshotStore.GetSnapshot("node-1", "selected"), Is.EqualTo("Charlie"));
        Assert.That(session.SentMessages, Has.Count.EqualTo(2));

        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("type").GetString(), Is.EqualTo("set_snapshot_value_response"));
        Assert.That(resp.RootElement.GetProperty("success").GetBoolean(), Is.True);

        using var push = JsonDocument.Parse(session.SentMessages[1]);
        Assert.That(push.RootElement.GetProperty("type").GetString(), Is.EqualTo("snapshot_saved"));
        Assert.That(push.RootElement.GetProperty("nodeInstanceId").GetString(), Is.EqualTo("node-1"));
        Assert.That(push.RootElement.GetProperty("portName").GetString(), Is.EqualTo("selected"));
        Assert.That(push.RootElement.GetProperty("valueString").GetString(), Is.EqualTo("Charlie"));
    }

    [Test]
    public async Task Handle_NumberBooleanNull_StoredWithExpectedClrTypes()
    {
        var handler = MakeHandler();
        var session = new TestSession();

        using (var doc = Msg("{\"nodeInstanceId\":\"n\",\"portName\":\"num\",\"value\":0.75}"))
            await handler.HandleAsync(session, doc.RootElement);
        using (var doc = Msg("{\"nodeInstanceId\":\"n\",\"portName\":\"flag\",\"value\":true}"))
            await handler.HandleAsync(session, doc.RootElement);
        using (var doc = Msg("{\"nodeInstanceId\":\"n\",\"portName\":\"none\",\"value\":null}"))
            await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SnapshotStore.GetSnapshot("n", "num"), Is.EqualTo(0.75d));
        Assert.That(session.SnapshotStore.GetSnapshot("n", "flag"), Is.EqualTo(true));
        Assert.That(session.SnapshotStore.GetSnapshot("n", "none"), Is.Null);
    }

    [Test]
    public async Task Handle_BlockedByCanSet_DoesNotChangeValueAndReportsBlocked()
    {
        var handler = MakeHandler();
        var session = new TestSession();
        session.SnapshotStore.SetSnapshot("node-1", "selected", "Original");
        session.SnapshotStore.CanSet = _ => false;
        using var doc = Msg("{\"nodeInstanceId\":\"node-1\",\"portName\":\"selected\",\"value\":\"Changed\"}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SnapshotStore.GetSnapshot("node-1", "selected"), Is.EqualTo("Original"));
        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(resp.RootElement.GetProperty("reason").GetString(), Is.EqualTo("blocked"));
    }

    [Test]
    public async Task Handle_MissingRequiredFields_SendsNothing()
    {
        var handler = MakeHandler();
        var session = new TestSession();

        using (var doc = Msg("{\"portName\":\"selected\",\"value\":\"x\"}"))
            await handler.HandleAsync(session, doc.RootElement);
        using (var doc = Msg("{\"nodeInstanceId\":\"node-1\",\"value\":\"x\"}"))
            await handler.HandleAsync(session, doc.RootElement);
        using (var doc = Msg("{\"nodeInstanceId\":\"node-1\",\"portName\":\"selected\"}"))
            await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SentMessages, Is.Empty);
    }

    [Test]
    public async Task Handle_ObjectValue_RejectedAsUnsupportedType()
    {
        var handler = MakeHandler();
        var session = new TestSession();
        using var doc = Msg("{\"nodeInstanceId\":\"node-1\",\"portName\":\"selected\",\"value\":{\"a\":1}}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SnapshotStore.GetSnapshot("node-1", "selected"), Is.Null);
        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(resp.RootElement.GetProperty("reason").GetString(), Is.EqualTo("unsupported_type"));
    }
}
