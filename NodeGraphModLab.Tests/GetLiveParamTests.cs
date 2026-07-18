using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class GetLiveParamTests
{
    [Test]
    public void InlineExecutionContext_ReadsFromLiveParamStore()
    {
        var store = new LiveParamStore();
        store.MergeParams("node-1", new Dictionary<string, object?> { ["scale"] = 2.5 });

        var parent = new MainThreadExecutionContext(
            "parent", new NullNgolLogger(), new PersistentNodeRunner(), liveParamStore: store);
        var ctx = new InlineExecutionContext(
            "node-1",
            new Dictionary<string, JsonElement>(),
            new Dictionary<string, object?>(),
            parent,
            liveParamStore: store);

        Assert.That(ctx.GetLiveParam("scale", 1.0), Is.EqualTo(2.5));
        Assert.That(ctx.GetLiveParam("missing", 9.0), Is.EqualTo(9.0));
    }

    private sealed class NullNgolLogger : INgolLogger
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
        public void LogDebug(string message) { }
    }
}
