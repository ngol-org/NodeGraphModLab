using System.Linq;
using System.Reflection;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.BuiltinNodes.Logic;
using NUnit.Framework;

namespace NodeGraphModLab.Tests;

/// <summary>
/// TextBoxNode（入力文字列をパススルーし、WebUI 側の型 ID 上書きで
/// ノード全体を編集可能テキストボックスとして表示する）の Execute() 単体テスト。
/// </summary>
[TestFixture]
public class TextBoxNodeTests
{
    [Test]
    public void TextBoxNode_HasCorrectNodeTypeAndPorts()
    {
        var attr = typeof(TextBoxNode).GetCustomAttribute<NodeTypeAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr!.Id, Is.EqualTo("ngol.logic.text_box"));
        Assert.That(attr.Category, Is.EqualTo("Logic/String"));

        var ports = typeof(TextBoxNode).GetCustomAttributes<NodePortAttribute>().ToList();
        Assert.That(ports.Any(p => p.Name == "text" && p.Direction == PortDirection.Input), Is.True);
        Assert.That(ports.Any(p => p.Name == "text" && p.Direction == PortDirection.Output), Is.True);

        // WebUI 本体無改修方式（型ID上書き）を使うため [NodeWebUi] は付与しない
        Assert.That(typeof(TextBoxNode).GetCustomAttribute<NodeWebUiAttribute>(), Is.Null);
    }

    [Test]
    public void TextBoxNode_PassesThroughConnectedInput()
    {
        var store = new InMemorySnapshotStore();
        var node = new TextBoxNode();
        var ctx = new TestExecutionContext("tb-001", store)
            .WithInput("text", "hello world");

        node.Execute(ctx);

        Assert.That(ctx.GetOutput("text"), Is.EqualTo("hello world"));
        Assert.That(store.GetSnapshot("tb-001", "text"), Is.EqualTo("hello world"));
    }

    [Test]
    public void TextBoxNode_UnconnectedInput_DefaultsToEmptyString()
    {
        var node = new TextBoxNode();
        var ctx = new TestExecutionContext("tb-002")
            .WithInput("text", null);

        Assert.DoesNotThrow(() => node.Execute(ctx));
        Assert.That(ctx.GetOutput("text"), Is.EqualTo(""));
    }

    [Test]
    public void TextBoxNode_NoSnapshotStore_DoesNotThrow()
    {
        var node = new TextBoxNode();
        var ctx = new TestExecutionContext("tb-003", snapshotStore: null)
            .WithInput("text", "abc");

        Assert.DoesNotThrow(() => node.Execute(ctx));
        Assert.That(ctx.GetOutput("text"), Is.EqualTo("abc"));
    }
}
