using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Server;
using NodeGraphModLab.Server.Handlers;

namespace NodeGraphModLab.Tests.Handlers;

[TestFixture]
public class SetSnapshotPinHandlerTests
{
    private static SetSnapshotPinHandler MakeHandler() => new();

    [Test]
    public async Task Handle_PinTrue_AddsNodeIdToPinnedSet()
    {
        var handler = MakeHandler();
        var session = new TestSession();
        using var doc = JsonDocument.Parse("{\"nodeInstanceId\":\"node-1\",\"pinned\":true}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.PinnedSnapshotNodeIds, Contains.Item("node-1"));
        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("pinned").GetBoolean(), Is.True);
    }

    [Test]
    public async Task Handle_PinFalse_RemovesNodeIdFromPinnedSet()
    {
        var handler = MakeHandler();
        var session = new TestSession();
        session.PinnedSnapshotNodeIds.Add("node-1");
        using var doc = JsonDocument.Parse("{\"nodeInstanceId\":\"node-1\",\"pinned\":false}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.PinnedSnapshotNodeIds, Does.Not.Contain("node-1"));
        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("pinned").GetBoolean(), Is.False);
    }

    [Test]
    public async Task Handle_MissingNodeId_SendsNothing()
    {
        var handler = MakeHandler();
        var session = new TestSession();
        using var doc = JsonDocument.Parse("{\"pinned\":true}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SentMessages, Is.Empty);
        Assert.That(session.PinnedSnapshotNodeIds, Is.Empty);
    }
}
