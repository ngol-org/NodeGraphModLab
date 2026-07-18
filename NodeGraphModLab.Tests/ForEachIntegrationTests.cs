using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.BuiltinNodes.Logic;

namespace NodeGraphModLab.Tests;

/// <summary>
/// Phase10: ForEachNode ループ統合テスト
/// IForEachController を使ったループ展開ロジックを検証する
/// GraphExecutor に依存しない純粋ロジックテスト
/// </summary>
[TestFixture]
public class ForEachIntegrationTests
{
    /// <summary>
    /// IForEachController を使ったシンプルなループエミュレーション
    /// GraphExecutor の ForEach ループ展開ロジックを再現
    /// </summary>
    private static List<(object? item, int index)> SimulateForEachLoop(
        IForEachController controller,
        IReadOnlyList<object?> items)
    {
        var fakeInputs = new Dictionary<string, object?> { ["items"] = items };
        var loopItems = controller.GetItems(fakeInputs);

        var results = new List<(object? item, int index)>();
        for (int i = 0; i < loopItems.Count; i++)
        {
            results.Add((loopItems[i], i));
        }
        return results;
    }

    [Test]
    public void ForEachLoop_StringItems_IteratesEachString()
    {
        var node = new ForEachNode();
        var items = new List<object?> { "apple", "banana", "cherry" };

        var results = SimulateForEachLoop(node, items);

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].item, Is.EqualTo("apple"));
        Assert.That(results[0].index, Is.EqualTo(0));
        Assert.That(results[1].item, Is.EqualTo("banana"));
        Assert.That(results[1].index, Is.EqualTo(1));
        Assert.That(results[2].item, Is.EqualTo("cherry"));
        Assert.That(results[2].index, Is.EqualTo(2));
    }

    [Test]
    public void ForEachLoop_NumberItems_IteratesEachNumber()
    {
        var node = new ForEachNode();
        var items = new List<object?> { 10.0, 20.0, 30.0, 40.0, 50.0 };

        var results = SimulateForEachLoop(node, items);

        Assert.That(results, Has.Count.EqualTo(5));
        for (int i = 0; i < results.Count; i++)
        {
            Assert.That(results[i].index, Is.EqualTo(i));
            Assert.That(results[i].item, Is.EqualTo(items[i]));
        }
    }

    [Test]
    public void ForEachLoop_EmptyList_NoIterations()
    {
        var node = new ForEachNode();
        var items = new List<object?>();

        var results = SimulateForEachLoop(node, items);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void ForEachLoop_SingleItem_OneIteration()
    {
        var node = new ForEachNode();
        var items = new List<object?> { "only" };

        var results = SimulateForEachLoop(node, items);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].item, Is.EqualTo("only"));
        Assert.That(results[0].index, Is.EqualTo(0));
    }

    [Test]
    public void ForEachLoop_NullItems_NoIterations()
    {
        var node = new ForEachNode();
        var fakeInputs = new Dictionary<string, object?> { ["items"] = null };

        var loopItems = node.GetItems(fakeInputs);

        Assert.That(loopItems, Is.Empty);
    }

    [Test]
    public void ForEachLoop_MixedTypeItems_IteratesAll()
    {
        var node = new ForEachNode();
        var items = new List<object?> { "string", 42.0, true, null, 3.14 };

        var results = SimulateForEachLoop(node, items);

        Assert.That(results, Has.Count.EqualTo(5));
        Assert.That(results[0].item, Is.EqualTo("string"));
        Assert.That(results[1].item, Is.EqualTo(42.0));
        Assert.That(results[2].item, Is.EqualTo(true));
        Assert.That(results[3].item, Is.Null);
        Assert.That(results[4].item, Is.EqualTo(3.14));
    }

    [Test]
    public void ForEachLoop_CurrentItemPortName_IsConsistent()
    {
        var node = new ForEachNode();
        // IForEachControllerのポート名が GraphExecutor で使われるキーと一致することを確認
        Assert.That(node.CurrentItemPortName, Is.EqualTo("current_item"));
        Assert.That(node.IndexPortName, Is.EqualTo("index"));
    }

    [Test]
    public void ForEachLoop_IndexIsZeroBased()
    {
        var node = new ForEachNode();
        var items = new List<object?> { "first", "second", "third" };

        var results = SimulateForEachLoop(node, items);

        Assert.That(results[0].index, Is.EqualTo(0));
        Assert.That(results[1].index, Is.EqualTo(1));
        Assert.That(results[2].index, Is.EqualTo(2));
    }

    [Test]
    public void ForEachLoop_NestedListInputNotFlattened()
    {
        // ネストしたリストは展開しない（リスト自体が1アイテム扱いになる）
        var node = new ForEachNode();
        var innerList1 = new List<object?> { 1.0, 2.0 };
        var innerList2 = new List<object?> { 3.0, 4.0 };
        var items = new List<object?> { innerList1, innerList2 };

        var results = SimulateForEachLoop(node, items);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].item, Is.SameAs(innerList1));
        Assert.That(results[1].item, Is.SameAs(innerList2));
    }
}
