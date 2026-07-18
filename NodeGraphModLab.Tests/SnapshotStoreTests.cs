using NodeGraphModLab.NodeAPI;
using NUnit.Framework;

namespace NodeGraphModLab.Tests;

/// <summary>
/// Phase 2: InMemorySnapshotStore および ISnapshotStore インターフェーステスト。
/// </summary>
[TestFixture]
public class SnapshotStoreTests
{
    private InMemorySnapshotStore _store = null!;

    [SetUp]
    public void SetUp() => _store = new InMemorySnapshotStore();

    [Test]
    public void HasSnapshot_ReturnsFalse_WhenNotSet()
    {
        Assert.That(_store.HasSnapshot("node-1", "value"), Is.False);
    }

    [Test]
    public void GetSnapshot_ReturnsNull_WhenNotSet()
    {
        Assert.That(_store.GetSnapshot("node-1", "value"), Is.Null);
    }

    [Test]
    public void SetAndGet_PrimitiveValue()
    {
        _store.SetSnapshot("node-1", "value", 42.0);
        Assert.That(_store.HasSnapshot("node-1", "value"), Is.True);
        Assert.That(_store.GetSnapshot("node-1", "value"), Is.EqualTo(42.0));
    }

    [Test]
    public void SetAndGet_StringValue()
    {
        _store.SetSnapshot("node-snap", "value", "hello");
        Assert.That(_store.GetSnapshot("node-snap", "value"), Is.EqualTo("hello"));
    }

    [Test]
    public void SetAndGet_NullValue()
    {
        _store.SetSnapshot("node-null", "value", null);
        Assert.That(_store.HasSnapshot("node-null", "value"), Is.True);
        Assert.That(_store.GetSnapshot("node-null", "value"), Is.Null);
    }

    [Test]
    public void Set_OverwritesPreviousValue()
    {
        _store.SetSnapshot("node-1", "value", "first");
        _store.SetSnapshot("node-1", "value", "second");
        Assert.That(_store.GetSnapshot("node-1", "value"), Is.EqualTo("second"));
    }

    [Test]
    public void DifferentPorts_StoredIndependently()
    {
        _store.SetSnapshot("node-1", "portA", "alpha");
        _store.SetSnapshot("node-1", "portB", "beta");
        Assert.That(_store.GetSnapshot("node-1", "portA"), Is.EqualTo("alpha"));
        Assert.That(_store.GetSnapshot("node-1", "portB"), Is.EqualTo("beta"));
    }

    [Test]
    public void DifferentNodes_StoredIndependently()
    {
        _store.SetSnapshot("node-A", "value", "fromA");
        _store.SetSnapshot("node-B", "value", "fromB");
        Assert.That(_store.GetSnapshot("node-A", "value"), Is.EqualTo("fromA"));
        Assert.That(_store.GetSnapshot("node-B", "value"), Is.EqualTo("fromB"));
    }

    [Test]
    public void Clear_RemovesAllSnapshots()
    {
        _store.SetSnapshot("node-1", "value", 1.0);
        _store.SetSnapshot("node-2", "value", 2.0);
        _store.Clear();
        Assert.That(_store.HasSnapshot("node-1", "value"), Is.False);
        Assert.That(_store.HasSnapshot("node-2", "value"), Is.False);
    }

    [Test]
    public void ImplementsISnapshotStore()
    {
        ISnapshotStore store = new InMemorySnapshotStore();
        store.SetSnapshot("n", "p", "val");
        Assert.That(store.GetSnapshot("n", "p"), Is.EqualTo("val"));
    }

    // ── GetAllCurrentEntries テスト ──────────────────────────────

    [Test]
    public void GetAllCurrentEntries_Empty_ReturnsEmptyList()
    {
        var entries = _store.GetAllCurrentEntries();
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void GetAllCurrentEntries_SingleEntry_ReturnsCorrectRecord()
    {
        _store.SetSnapshot("node-1", "portA", 42.0);
        var entries = _store.GetAllCurrentEntries();

        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].NodeInstanceId, Is.EqualTo("node-1"));
        Assert.That(entries[0].PortName, Is.EqualTo("portA"));
        Assert.That(entries[0].Value, Is.EqualTo(42.0));
    }

    [Test]
    public void GetAllCurrentEntries_MultipleEntries_AllReturned()
    {
        _store.SetSnapshot("node-1", "portA", "alpha");
        _store.SetSnapshot("node-1", "portB", "beta");
        _store.SetSnapshot("node-2", "portA", 99);

        var entries = _store.GetAllCurrentEntries();
        Assert.That(entries.Count, Is.EqualTo(3));

        // 各エントリが SnapshotEntryRecord 型であることを確認
        Assert.That(entries, Is.All.InstanceOf<SnapshotEntryRecord>());

        // 期待するエントリがすべて含まれるか確認
        Assert.That(entries.Any(e => e.NodeInstanceId == "node-1" && e.PortName == "portA" && Equals(e.Value, "alpha")), Is.True);
        Assert.That(entries.Any(e => e.NodeInstanceId == "node-1" && e.PortName == "portB" && Equals(e.Value, "beta")), Is.True);
        Assert.That(entries.Any(e => e.NodeInstanceId == "node-2" && e.PortName == "portA" && Equals(e.Value, 99)), Is.True);
    }

    [Test]
    public void GetAllCurrentEntries_NullValue_RecordHasNullValue()
    {
        _store.SetSnapshot("node-1", "portA", null);
        var entries = _store.GetAllCurrentEntries();

        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].Value, Is.Null);
    }

    [Test]
    public void GetAllCurrentEntries_AfterOverwrite_ReturnsLatestValue()
    {
        _store.SetSnapshot("node-1", "portA", "first");
        _store.SetSnapshot("node-1", "portA", "second");
        var entries = _store.GetAllCurrentEntries();

        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries[0].Value, Is.EqualTo("second"));
    }

    [Test]
    public void GetAllCurrentEntries_AfterClear_ReturnsEmpty()
    {
        _store.SetSnapshot("node-1", "portA", "value");
        _store.Clear();
        var entries = _store.GetAllCurrentEntries();
        Assert.That(entries, Is.Empty);
    }

    // ── GetFirstHistoryForNode テスト ─────────────────────────────

    [Test]
    public void GetFirstHistoryForNode_NoEntry_ReturnsNull()
    {
        var result = _store.GetFirstHistoryForNode("node-1");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetFirstHistoryForNode_WithSnapshot_ReturnsPortHistoryResult()
    {
        _store.SetSnapshot("node-1", "portA", "v1");
        _store.SetSnapshot("node-1", "portA", "v2"); // v1 が履歴に入る

        var result = _store.GetFirstHistoryForNode("node-1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<PortHistoryResult>());
        Assert.That(result!.PortName, Is.EqualTo("portA"));
        Assert.That(result.Entries.Count, Is.EqualTo(1));
        Assert.That(result.Entries[0].ValueString, Is.EqualTo("v1"));
    }

    [Test]
    public void GetFirstHistoryForNode_NoHistory_ReturnsEmptyEntries()
    {
        // 1回しか SetSnapshot していない（上書きなし）→ 履歴は空だが _store にある
        _store.SetSnapshot("node-1", "portA", "only");

        var result = _store.GetFirstHistoryForNode("node-1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PortName, Is.EqualTo("portA"));
        Assert.That(result.Entries, Is.Empty);
    }
}
