using NUnit.Framework;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class DebugLogStoreTests
{
    [Test]
    public void Add_GetRecent_ReturnsNewestFirst()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "first");
        store.Add("console", "log", "second");

        var recent = store.GetRecent(10);
        Assert.That(recent.Count, Is.EqualTo(2));
        Assert.That(recent[0].Message, Is.EqualTo("second"));
        Assert.That(recent[1].Message, Is.EqualTo("first"));
    }

    [Test]
    public void Add_ExceedsMaxCount_DropsOldest()
    {
        var store = new DebugLogStore();
        for (var i = 0; i < 205; i++)
            store.Add("console", "log", $"msg-{i}");

        var recent = store.GetRecent(300);
        Assert.That(recent.Count, Is.EqualTo(200));
        Assert.That(recent[0].Message, Is.EqualTo("msg-204"));
        Assert.That(recent[^1].Message, Is.EqualTo("msg-5"));
    }

    [Test]
    public void Clear_RemovesAllEntries()
    {
        var store = new DebugLogStore();
        store.Add("dom_event", "log", "event");
        store.Clear();
        Assert.That(store.GetRecent(10), Is.Empty);
    }

    [Test]
    public void GetFiltered_ByKind_ReturnsOnlyMatching()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "hello");
        store.Add("dom_event", "log", "{\"type\":\"mousedown\"}");

        var result = store.GetFiltered(new DebugLogFilter { Kind = "dom_event" }, 10);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Kind, Is.EqualTo("dom_event"));
    }

    [Test]
    public void GetFiltered_ByLevel_ReturnsOnlyMatching()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "info message");
        store.Add("console", "error", "boom");

        var result = store.GetFiltered(new DebugLogFilter { Level = "error" }, 10);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Message, Is.EqualTo("boom"));
    }

    [Test]
    public void GetFiltered_ByMessageContains_IsCaseInsensitive()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "ContextMenu opened");
        store.Add("console", "log", "unrelated");

        var result = store.GetFiltered(new DebugLogFilter { MessageContains = "contextmenu" }, 10);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Message, Is.EqualTo("ContextMenu opened"));
    }

    [Test]
    public void GetFiltered_CombinedFilters_RequireAllToMatch()
    {
        var store = new DebugLogStore();
        store.Add("dom_event", "log", "contextmenu on node");
        store.Add("dom_event", "log", "mousedown on pane");
        store.Add("console", "log", "contextmenu mentioned in log");

        var result = store.GetFiltered(
            new DebugLogFilter { Kind = "dom_event", MessageContains = "contextmenu" }, 10);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Message, Is.EqualTo("contextmenu on node"));
    }

    [Test]
    public void GetFiltered_RespectsCountAfterFiltering()
    {
        var store = new DebugLogStore();
        for (var i = 0; i < 5; i++) store.Add("dom_event", "log", $"evt-{i}");
        store.Add("console", "log", "noise");

        var result = store.GetFiltered(new DebugLogFilter { Kind = "dom_event" }, 2);

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].Message, Is.EqualTo("evt-4"));
        Assert.That(result[1].Message, Is.EqualTo("evt-3"));
    }

    [Test]
    public void GetFiltered_ByDomEventType_MatchesJsonTypeFieldOnly()
    {
        var store = new DebugLogStore();
        store.Add("dom_event", "log", "{\"type\":\"contextmenu\",\"target\":{\"className\":\"foo\"}}");
        store.Add("dom_event", "log", "{\"type\":\"mousedown\"}");
        store.Add("console", "log", "contextmenu mentioned in a plain log line");

        var result = store.GetFiltered(new DebugLogFilter { DomEventType = "contextmenu" }, 10);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Message, Does.Contain("\"type\":\"contextmenu\""));
    }

    [Test]
    public void GetFiltered_ByDomEventType_NonJsonMessage_IsExcludedNotThrown()
    {
        var store = new DebugLogStore();
        store.Add("dom_event", "log", "not valid json");

        var result = store.GetFiltered(new DebugLogFilter { DomEventType = "contextmenu" }, 10);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetFiltered_BySinceMsUntilMs_NarrowsToTimeRange()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "older");
        Thread.Sleep(5);
        var midMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Thread.Sleep(5);
        store.Add("console", "log", "newer");

        var result = store.GetFiltered(new DebugLogFilter { SinceMs = midMs }, 10);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Message, Is.EqualTo("newer"));
    }

    [Test]
    public void GetFiltered_ByLevelAtLeast_ExcludesLowerSeverity()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "info");
        store.Add("console", "warn", "careful");
        store.Add("console", "error", "boom");

        var result = store.GetFiltered(new DebugLogFilter { LevelAtLeast = "warn" }, 10);

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.Select(r => r.Message), Is.EquivalentTo(new[] { "careful", "boom" }));
    }

    [Test]
    public void GetFiltered_ByMessageRegex_MatchesPattern()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "node-123 selected");
        store.Add("console", "log", "no id here");

        var result = store.GetFiltered(new DebugLogFilter { MessageRegex = @"node-\d+" }, 10);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Message, Is.EqualTo("node-123 selected"));
    }

    [Test]
    public void GetFiltered_ByMessageRegex_InvalidPattern_IsExcludedNotThrown()
    {
        var store = new DebugLogStore();
        store.Add("console", "log", "hello");

        var result = store.GetFiltered(new DebugLogFilter { MessageRegex = "(unterminated" }, 10);

        Assert.That(result, Is.Empty);
    }
}
