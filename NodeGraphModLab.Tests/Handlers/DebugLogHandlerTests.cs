using System.Collections.Concurrent;
using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Server;
using NodeGraphModLab.Server.Handlers;

namespace NodeGraphModLab.Tests.Handlers;

[TestFixture]
public class DebugLogHandlerTests
{
    private static HandlerContext MakeCtx(IDebugLogStore? store = null)
    {
        return new HandlerContext(
            new NodeRegistry(),
            new LiveParamStore(),
            store ?? new DebugLogStore(),
            log: null,
            graphSaveDir: "",
            dynamicNodesDir: "",
            nodesDir: "",
            nodePacksDir: "",
            new ConcurrentDictionary<string, string>(),
            new ConcurrentQueue<PendingExecution>(),
            new PersistentNodeRunner(),
            () => null,
            new GraphExecutor(new NodeRegistry()));
    }

    private static JsonDocument Msg(string json) => JsonDocument.Parse(json);

    [Test]
    public async Task DebugLogEntry_AddsToStore()
    {
        var store = new DebugLogStore();
        var handler = new DebugLogEntryHandler(MakeCtx(store));
        using var doc = Msg("{\"kind\":\"console\",\"level\":\"warn\",\"message\":\"hello\"}");

        await handler.HandleAsync(new TestSession(), doc.RootElement);

        var entries = store.GetRecent(10);
        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].Kind, Is.EqualTo("console"));
        Assert.That(entries[0].Level, Is.EqualTo("warn"));
        Assert.That(entries[0].Message, Is.EqualTo("hello"));
    }

    [Test]
    public async Task DebugLogEntry_EmptyMessage_IsIgnored()
    {
        var store = new DebugLogStore();
        var handler = new DebugLogEntryHandler(MakeCtx(store));
        using var doc = Msg("{\"message\":\"\"}");

        await handler.HandleAsync(new TestSession(), doc.RootElement);

        Assert.That(store.GetRecent(10), Is.Empty);
    }

    [Test]
    public async Task GetDebugLog_ReturnsRecentEntries()
    {
        var store = new DebugLogStore();
        store.Add("dom_event", "log", "{\"type\":\"mousedown\"}");
        store.Add("console", "error", "boom");

        var handler = new GetDebugLogHandler(MakeCtx(store));
        var session = new TestSession();
        using var doc = Msg("{\"count\":1}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SentMessages.Count, Is.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("type").GetString(), Is.EqualTo("get_debug_log_response"));
        var entries = resp.RootElement.GetProperty("entries");
        Assert.That(entries.GetArrayLength(), Is.EqualTo(1));
        Assert.That(entries[0].GetProperty("message").GetString(), Is.EqualTo("boom"));
    }

    [Test]
    public async Task GetDebugLog_DefaultCount_IsTen()
    {
        var store = new DebugLogStore();
        for (var i = 0; i < 15; i++) store.Add("console", "log", $"msg-{i}");

        var handler = new GetDebugLogHandler(MakeCtx(store));
        var session = new TestSession();
        using var doc = Msg("{}");

        await handler.HandleAsync(session, doc.RootElement);

        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("entries").GetArrayLength(), Is.EqualTo(10));
    }

    [Test]
    public async Task GetDebugLog_KindAndMessageContainsFilter_NarrowsResults()
    {
        var store = new DebugLogStore();
        store.Add("dom_event", "log", "contextmenu on node");
        store.Add("dom_event", "log", "mousedown on pane");
        store.Add("console", "log", "contextmenu mentioned in log");

        var handler = new GetDebugLogHandler(MakeCtx(store));
        var session = new TestSession();
        using var doc = Msg("{\"kind\":\"dom_event\",\"messageContains\":\"contextmenu\",\"count\":5}");

        await handler.HandleAsync(session, doc.RootElement);

        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        var entries = resp.RootElement.GetProperty("entries");
        Assert.That(entries.GetArrayLength(), Is.EqualTo(1));
        Assert.That(entries[0].GetProperty("message").GetString(), Is.EqualTo("contextmenu on node"));
    }

    [Test]
    public async Task GetDebugLog_DomEventTypeFilter_MatchesStructuredFieldOnly()
    {
        var store = new DebugLogStore();
        store.Add("dom_event", "log", "{\"type\":\"contextmenu\"}");
        store.Add("dom_event", "log", "{\"type\":\"mousedown\"}");

        var handler = new GetDebugLogHandler(MakeCtx(store));
        var session = new TestSession();
        using var doc = Msg("{\"domEventType\":\"contextmenu\",\"count\":5}");

        await handler.HandleAsync(session, doc.RootElement);

        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        var entries = resp.RootElement.GetProperty("entries");
        Assert.That(entries.GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public async Task GetDebugLog_LevelAtLeastFilter_ExcludesLowerSeverity()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "info");
        store.Add("console", "error", "boom");

        var handler = new GetDebugLogHandler(MakeCtx(store));
        var session = new TestSession();
        using var doc = Msg("{\"levelAtLeast\":\"warn\",\"count\":5}");

        await handler.HandleAsync(session, doc.RootElement);

        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        var entries = resp.RootElement.GetProperty("entries");
        Assert.That(entries.GetArrayLength(), Is.EqualTo(1));
        Assert.That(entries[0].GetProperty("message").GetString(), Is.EqualTo("boom"));
    }

    [Test]
    public async Task GetDebugLog_MessageRegexFilter_NarrowsResults()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "node-42 clicked");
        store.Add("console", "log", "unrelated");

        var handler = new GetDebugLogHandler(MakeCtx(store));
        var session = new TestSession();
        using var doc = Msg("{\"messageRegex\":\"node-\\\\d+\",\"count\":5}");

        await handler.HandleAsync(session, doc.RootElement);

        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        var entries = resp.RootElement.GetProperty("entries");
        Assert.That(entries.GetArrayLength(), Is.EqualTo(1));
        Assert.That(entries[0].GetProperty("message").GetString(), Is.EqualTo("node-42 clicked"));
    }

    [Test]
    public async Task GetDebugLog_SinceMsUntilMsFilter_NarrowsToTimeRange()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "old");
        Thread.Sleep(5);
        var midMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Thread.Sleep(5);
        store.Add("console", "log", "recent");

        var handler = new GetDebugLogHandler(MakeCtx(store));
        var session = new TestSession();
        using var doc = Msg($"{{\"sinceMs\":{midMs},\"count\":5}}");

        await handler.HandleAsync(session, doc.RootElement);

        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        var entries = resp.RootElement.GetProperty("entries");
        Assert.That(entries.GetArrayLength(), Is.EqualTo(1));
        Assert.That(entries[0].GetProperty("message").GetString(), Is.EqualTo("recent"));
    }

    [Test]
    public async Task GetDebugLog_LongMessage_IsTruncated()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", new string('x', 3000));

        var handler = new GetDebugLogHandler(MakeCtx(store));
        var session = new TestSession();
        using var doc = Msg("{\"count\":1}");

        await handler.HandleAsync(session, doc.RootElement);

        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        var message = resp.RootElement.GetProperty("entries")[0].GetProperty("message").GetString()!;
        Assert.That(message.Length, Is.EqualTo(2048 + "...[truncated]".Length));
        Assert.That(message, Does.EndWith("...[truncated]"));
    }
}
