using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class GraphDefinitionTests
{
    [Test]
    public void NodeGraph_JsonRoundtrip_PreservesAllFields()
    {
        var graph = new NodeGraph
        {
            Id = "test-graph-1",
            Name = "Test Graph",
            Description = "Unit test graph",
            Nodes = new List<NodeInstance>
            {
                new NodeInstance
                {
                    InstanceId = "node-1",
                    NodeTypeId = "gol.test.dummy",
                    NodeTypeVersion = "1.2.3",
                    Position = new NodePosition { X = 100, Y = 200 },
                    ParamValues = new Dictionary<string, JsonElement>
                    {
                        ["myParam"] = JsonDocument.Parse("\"hello\"").RootElement
                    }
                }
            },
            Connections = new List<NodeConnection>
            {
                new NodeConnection
                {
                    FromNodeInstanceId = "node-1",
                    FromPortName = "output",
                    ToNodeInstanceId = "node-2",
                    ToPortName = "input"
                }
            }
        };

        var json = graph.ToJson();
        var restored = NodeGraph.FromJson(json);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Id, Is.EqualTo("test-graph-1"));
        Assert.That(restored.Name, Is.EqualTo("Test Graph"));
        Assert.That(restored.Nodes, Has.Count.EqualTo(1));
        Assert.That(restored.Nodes[0].InstanceId, Is.EqualTo("node-1"));
        Assert.That(restored.Nodes[0].NodeTypeId, Is.EqualTo("gol.test.dummy"));
        Assert.That(restored.Nodes[0].NodeTypeVersion, Is.EqualTo("1.2.3"));
        Assert.That(restored.Nodes[0].Position.X, Is.EqualTo(100f));
        Assert.That(restored.Nodes[0].Position.Y, Is.EqualTo(200f));
        Assert.That(restored.Connections, Has.Count.EqualTo(1));
        Assert.That(restored.Connections[0].FromPortName, Is.EqualTo("output"));
        Assert.That(restored.Connections[0].ToPortName, Is.EqualTo("input"));
    }

    [Test]
    public void NodeGraph_EmptyGraph_SerializesAndDeserializes()
    {
        var graph = new NodeGraph { Id = "empty-1", Name = "Empty" };
        var json = graph.ToJson();
        var restored = NodeGraph.FromJson(json);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Nodes, Is.Empty);
        Assert.That(restored.Connections, Is.Empty);
    }

    [Test]
    public void NodeGraph_FromJson_WithInvalidJson_ReturnsNull()
    {
        // System.Text.Json はデシリアライズ失敗時に例外を投げる
        Assert.Throws<JsonException>(() => NodeGraph.FromJson("not valid json"));
    }

    [Test]
    public void NodeGraph_NewInstance_HasUniqueId()
    {
        var g1 = new NodeGraph();
        var g2 = new NodeGraph();
        Assert.That(g1.Id, Is.Not.EqualTo(g2.Id));
    }

    [Test]
    public void NodeGraph_JsonRoundtrip_PreservesAnnotations()
    {
        var graph = new NodeGraph
        {
            Id = "annot-test",
            Name = "Annotation Test",
            Annotations = new List<NodeAnnotation>
            {
                new NodeAnnotation
                {
                    Id = "a1",
                    Text = "テストメモ",
                    Position = new NodePosition { X = 50, Y = 75 },
                    Width = 200,
                    Height = 100,
                    Color = "#fffde7"
                }
            }
        };

        var json = graph.ToJson();
        var restored = NodeGraph.FromJson(json);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Annotations, Has.Count.EqualTo(1));
        Assert.That(restored.Annotations![0].Id, Is.EqualTo("a1"));
        Assert.That(restored.Annotations![0].Text, Is.EqualTo("テストメモ"));
        Assert.That(restored.Annotations![0].Position.X, Is.EqualTo(50f));
        Assert.That(restored.Annotations![0].Position.Y, Is.EqualTo(75f));
        Assert.That(restored.Annotations![0].Width, Is.EqualTo(200));
        Assert.That(restored.Annotations![0].Height, Is.EqualTo(100));
        Assert.That(restored.Annotations![0].Color, Is.EqualTo("#fffde7"));
    }

    [Test]
    public void NodeGraph_JsonRoundtrip_PreservesSchemaVersion()
    {
        var graph = new NodeGraph { Id = "sv-test", Name = "SchemaVersion Test" };
        var json = graph.ToJson();
        var restored = NodeGraph.FromJson(json);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.SchemaVersion, Is.EqualTo("0.2.0"));
    }

    [Test]
    public void NodeGraph_FromJson_LegacyGraphWithoutSchemaVersion_LoadsSuccessfully()
    {
        // schemaVersion フィールドのない旧グラフ JSON を読み込めることを確認
        var legacyJson = """
            {
              "id": "legacy-1",
              "name": "Legacy Graph",
              "description": "",
              "version": 1,
              "createdAt": "2026-01-01T00:00:00+00:00",
              "nodes": [],
              "connections": [],
              "fragmentLinks": []
            }
            """;

        var restored = NodeGraph.FromJson(legacyJson);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Id, Is.EqualTo("legacy-1"));
        // schemaVersion なしで FromJson した場合、デフォルト値 "0.2.0" が設定される
        Assert.That(restored.SchemaVersion, Is.EqualTo("0.2.0"));
        Assert.That(restored.Annotations, Is.Empty);
    }
}

[TestFixture]
public class NodeTypeAttributeTests
{
    [Test]
    public void NodeTypeAttribute_ValidArgs_StoresProperties()
    {
        var attr = new NodeTypeAttribute("gol.test.node", "Test/Category", "Test Node");
        Assert.That(attr.Id, Is.EqualTo("gol.test.node"));
        Assert.That(attr.Category, Is.EqualTo("Test/Category"));
        Assert.That(attr.DisplayName, Is.EqualTo("Test Node"));
        Assert.That(attr.Version, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void NodeTypeAttribute_VersionProperty_CanBeOverridden()
    {
        var attr = new NodeTypeAttribute("gol.test.node", "Test/Category", "Test Node")
        {
            Version = "2.1.0"
        };
        Assert.That(attr.Version, Is.EqualTo("2.1.0"));
    }

    [Test]
    public void NodeTypeAttribute_EmptyId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new NodeTypeAttribute("", "Category", "Name"));
    }

    [Test]
    public void NodeTypeAttribute_AppliedToClass_CanBeRetrievedByReflection()
    {
        var attr = typeof(DummyNodeWithAttribute)
            .GetCustomAttributes(typeof(NodeTypeAttribute), false)
            .OfType<NodeTypeAttribute>()
            .FirstOrDefault();

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr!.Id, Is.EqualTo("gol.test.dummy"));
        Assert.That(attr.Category, Is.EqualTo("Test"));
        Assert.That(attr.DisplayName, Is.EqualTo("Dummy Node"));
    }

    [Test]
    public void NodePortAttribute_AppliedToClass_CanBeRetrievedByReflection()
    {
        var ports = typeof(DummyNodeWithAttribute)
            .GetCustomAttributes(typeof(NodePortAttribute), false)
            .OfType<NodePortAttribute>()
            .ToList();

        Assert.That(ports, Has.Count.EqualTo(2));
        Assert.That(ports.Any(p => p.Name == "input" && p.Direction == PortDirection.Input), Is.True);
        Assert.That(ports.Any(p => p.Name == "output" && p.Direction == PortDirection.Output), Is.True);
    }
}

// ---- テスト用ノード定義 ----

[NodeType("gol.test.dummy", "Test", "Dummy Node", Description = "A dummy node for testing")]
[NodePort("input", PortDirection.Input, "object")]
[NodePort("output", PortDirection.Output, "object")]
internal class DummyNodeWithAttribute : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("input");
        ctx.SetPortValue("output", value);
    }
}
