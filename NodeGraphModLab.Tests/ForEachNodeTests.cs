using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.BuiltinNodes.Logic;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class ForEachNodeTests
{
    private ForEachNode _node = null!;

    [SetUp]
    public void SetUp()
    {
        _node = new ForEachNode();
    }

    [Test]
    public void ForEachNode_ImplementsIForEachController()
    {
        Assert.That(_node, Is.InstanceOf<IForEachController>());
    }

    [Test]
    public void ForEachNode_PortNames_AreCorrect()
    {
        Assert.That(_node.CurrentItemPortName, Is.EqualTo("current_item"));
        Assert.That(_node.IndexPortName, Is.EqualTo("index"));
    }

    [Test]
    public void GetItems_WithReadOnlyList_ReturnsAllItems()
    {
        var items = new List<object?> { "a", "b", "c" };
        var input = new Dictionary<string, object?> { ["items"] = (IReadOnlyList<object?>)items };
        var result = _node.GetItems(input);
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0], Is.EqualTo("a"));
        Assert.That(result[2], Is.EqualTo("c"));
    }

    [Test]
    public void GetItems_WithList_ReturnsAllItems()
    {
        var items = new List<object?> { 1.0, 2.0, 3.0 };
        var input = new Dictionary<string, object?> { ["items"] = items };
        var result = _node.GetItems(input);
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void GetItems_WithEnumerable_ReturnsAllItems()
    {
        var items = Enumerable.Range(1, 5).Cast<object?>();
        var input = new Dictionary<string, object?> { ["items"] = items };
        var result = _node.GetItems(input);
        Assert.That(result, Has.Count.EqualTo(5));
    }

    [Test]
    public void GetItems_WithNull_ReturnsEmpty()
    {
        var input = new Dictionary<string, object?> { ["items"] = null };
        var result = _node.GetItems(input);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_WithMissingKey_ReturnsEmpty()
    {
        var result = _node.GetItems(new Dictionary<string, object?>());
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_WithSingleNonListValue_WrapsInList()
    {
        var input = new Dictionary<string, object?> { ["items"] = "single" };
        var result = _node.GetItems(input);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("single"));
    }

    [Test]
    public void ForEachNode_HasCorrectNodeTypeId()
    {
        var attr = typeof(ForEachNode)
            .GetCustomAttributes(typeof(NodeTypeAttribute), false)
            .OfType<NodeTypeAttribute>()
            .FirstOrDefault();

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr!.Id, Is.EqualTo("ngol.logic.foreach"));
        Assert.That(attr.Category, Is.EqualTo("Logic/Control"));
    }

    [Test]
    public void ForEachNode_HasRequiredPorts()
    {
        var ports = typeof(ForEachNode)
            .GetCustomAttributes(typeof(NodePortAttribute), false)
            .OfType<NodePortAttribute>()
            .ToList();

        Assert.That(ports.Any(p => p.Name == "items"        && p.Direction == PortDirection.Input),  Is.True, "items input port missing");
        Assert.That(ports.Any(p => p.Name == "current_item" && p.Direction == PortDirection.Output), Is.True, "current_item output port missing");
        Assert.That(ports.Any(p => p.Name == "index"        && p.Direction == PortDirection.Output), Is.True, "index output port missing");
        Assert.That(ports.Any(p => p.Name == "count"        && p.Direction == PortDirection.Output), Is.True, "count output port missing");
    }
}
