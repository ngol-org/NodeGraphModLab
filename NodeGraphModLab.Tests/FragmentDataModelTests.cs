using System.Text.Json;
using NodeGraphModLab.NodeAPI;
using NUnit.Framework;

namespace NodeGraphModLab.Tests;

/// <summary>
/// Phase 1: FragmentDefinition / FragmentLink のデータモデルおよび JSON ラウンドトリップテスト。
/// </summary>
[TestFixture]
public class FragmentDataModelTests
{
    [Test]
    public void NodeGraph_HasEmptyFragmentsAndLinks_ByDefault()
    {
        var graph = new NodeGraph();
        Assert.That(graph.Fragments, Is.Not.Null);
        Assert.That(graph.Fragments, Is.Empty);
        Assert.That(graph.FragmentLinks, Is.Not.Null);
        Assert.That(graph.FragmentLinks, Is.Empty);
    }

    [Test]
    public void FragmentDefinition_DefaultValues()
    {
        var frag = new FragmentDefinition();
        Assert.That(frag.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(frag.Name, Is.EqualTo("Fragment"));
        Assert.That(frag.NodeInstanceIds, Is.Empty);
    }

    [Test]
    public void FragmentLink_DefaultValues()
    {
        var link = new FragmentLink();
        Assert.That(link.SourcePortName, Is.EqualTo("value"));
        Assert.That(link.SourceSnapshotNodeInstanceId, Is.Empty);
        Assert.That(link.ToNodeInstanceId, Is.Empty);
        Assert.That(link.ToPortName, Is.Empty);
    }

    [Test]
    public void NodeGraph_JsonRoundtrip_WithFragmentsAndLinks()
    {
        var fragIdA = "frag-a";
        var fragIdB = "frag-b";
        var snapNodeId = "snap-001";
        var targetNodeId = "node-xyz";

        var graph = new NodeGraph
        {
            Id = "graph-test-001",
            Name = "テストグラフ",
            Fragments =
            {
                new FragmentDefinition { Id = fragIdA, Name = "Fragment A", NodeInstanceIds = { snapNodeId } },
                new FragmentDefinition { Id = fragIdB, Name = "Fragment B", NodeInstanceIds = { targetNodeId } }
            },
            FragmentLinks =
            {
                new FragmentLink
                {
                    SourceSnapshotNodeInstanceId = snapNodeId,
                    SourcePortName = "value",
                    ToNodeInstanceId = targetNodeId,
                    ToPortName = "gameObject"
                }
            }
        };

        var json = graph.ToJson();
        Assert.That(json, Does.Contain("fragments"));
        Assert.That(json, Does.Contain("fragmentLinks"));
        Assert.That(json, Does.Contain("Fragment A"));
        Assert.That(json, Does.Contain("Fragment B"));

        var restored = NodeGraph.FromJson(json);
        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Fragments, Has.Count.EqualTo(2));
        Assert.That(restored.Fragments[0].Id, Is.EqualTo(fragIdA));
        Assert.That(restored.Fragments[0].Name, Is.EqualTo("Fragment A"));
        Assert.That(restored.Fragments[0].NodeInstanceIds, Contains.Item(snapNodeId));
        Assert.That(restored.Fragments[1].Id, Is.EqualTo(fragIdB));

        Assert.That(restored.FragmentLinks, Has.Count.EqualTo(1));
        Assert.That(restored.FragmentLinks[0].SourceSnapshotNodeInstanceId, Is.EqualTo(snapNodeId));
        Assert.That(restored.FragmentLinks[0].SourcePortName, Is.EqualTo("value"));
        Assert.That(restored.FragmentLinks[0].ToNodeInstanceId, Is.EqualTo(targetNodeId));
        Assert.That(restored.FragmentLinks[0].ToPortName, Is.EqualTo("gameObject"));
    }

    [Test]
    public void NodeGraph_V1Compat_FromJsonWithNoFragments()
    {
        // fragments/fragmentLinks フィールドが存在しない旧 v1 JSON でも正常に読み込める
        var v1Json = """
            {
              "id": "old-graph",
              "name": "Legacy Graph",
              "description": "",
              "version": 1,
              "createdAt": "2024-01-01T00:00:00+00:00",
              "nodes": [],
              "connections": []
            }
            """;

        var graph = NodeGraph.FromJson(v1Json);
        Assert.That(graph, Is.Not.Null);
        Assert.That(graph!.Fragments, Is.Empty);
        Assert.That(graph.FragmentLinks, Is.Empty);
    }

    [Test]
    public void FragmentDefinition_MultipleNodeIds_Preserved()
    {
        var ids = new[] { "node-1", "node-2", "node-3" };
        var frag = new FragmentDefinition { Id = "f1", Name = "Test", NodeInstanceIds = ids.ToList() };

        var graph = new NodeGraph { Fragments = { frag } };
        var restored = NodeGraph.FromJson(graph.ToJson());

        Assert.That(restored!.Fragments[0].NodeInstanceIds, Is.EquivalentTo(ids));
    }
}
