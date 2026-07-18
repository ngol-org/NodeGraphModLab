using NUnit.Framework;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class LiveParamStoreTests
{
    [Test]
    public void MergeParams_PartialMerge_OverwritesExistingKeys()
    {
        var store = new LiveParamStore();
        store.MergeParams("node-1", new Dictionary<string, object?> { ["a"] = 1.0, ["b"] = true });
        store.MergeParams("node-1", new Dictionary<string, object?> { ["b"] = false, ["c"] = "x" });

        Assert.That(store.TryGet("node-1", "a", out var a), Is.True);
        Assert.That(a, Is.EqualTo(1.0));
        Assert.That(store.TryGet("node-1", "b", out var b), Is.True);
        Assert.That(b, Is.EqualTo(false));
        Assert.That(store.TryGet("node-1", "c", out var c), Is.True);
        Assert.That(c, Is.EqualTo("x"));
    }

    [Test]
    public void ClearNode_RemovesOnlyTargetNode()
    {
        var store = new LiveParamStore();
        store.MergeParams("node-1", new Dictionary<string, object?> { ["x"] = 1.0 });
        store.MergeParams("node-2", new Dictionary<string, object?> { ["y"] = 2.0 });

        store.ClearNode("node-1");

        Assert.That(store.TryGet("node-1", "x", out _), Is.False);
        Assert.That(store.TryGet("node-2", "y", out var y), Is.True);
        Assert.That(y, Is.EqualTo(2.0));
    }

    [Test]
    public void ConvertValue_CoercesNumericTypes()
    {
        Assert.That(LiveParamStore.ConvertValue(3.5, 0f), Is.EqualTo(3.5f).Within(0.001));
        Assert.That(LiveParamStore.ConvertValue(2.9, 0), Is.EqualTo(2));
        Assert.That(LiveParamStore.ConvertValue(1.0, false), Is.True);
    }
}
