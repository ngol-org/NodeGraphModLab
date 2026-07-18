using NUnit.Framework;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class NodeWebUiAttributeTests
{
    [Test]
    public void ToJson_PluginIdOnly_ProducesMinimalJson()
    {
        var attr = new NodeWebUiAttribute("ngol.webui.dropdown");

        Assert.That(attr.ToJson(), Is.EqualTo("{\"pluginId\":\"ngol.webui.dropdown\"}"));
    }

    [Test]
    public void ToJson_WithoutExtraJson_MatchesLegacyOutput()
    {
        var attr = new NodeWebUiAttribute("ngol.webui.dropdown")
        {
            OptionsFromSnapshot = "options",
            BindTo = "selected"
        };

        Assert.That(attr.ToJson(), Is.EqualTo(
            "{\"pluginId\":\"ngol.webui.dropdown\",\"optionsFromSnapshot\":\"options\",\"bindTo\":\"selected\"}"));
    }

    [Test]
    public void ToJson_WithExtraJson_AppendsExtraField()
    {
        var attr = new NodeWebUiAttribute("my.webui.gauge")
        {
            OptionsFromSnapshot = "value",
            ExtraJson = "{\"max\":\"200\"}"
        };

        Assert.That(attr.ToJson(), Is.EqualTo(
            "{\"pluginId\":\"my.webui.gauge\",\"optionsFromSnapshot\":\"value\",\"extra\":{\"max\":\"200\"}}"));
    }

    [Test]
    public void ToJson_WithExtraJson_IsValidJsonWithParsedExtra()
    {
        var attr = new NodeWebUiAttribute("my.webui.gauge")
        {
            ExtraJson = "{\"max\":\"200\",\"label\":\"Speed\"}"
        };

        using var doc = System.Text.Json.JsonDocument.Parse(attr.ToJson());
        var root = doc.RootElement;

        Assert.That(root.GetProperty("pluginId").GetString(), Is.EqualTo("my.webui.gauge"));
        Assert.That(root.GetProperty("extra").GetProperty("max").GetString(), Is.EqualTo("200"));
        Assert.That(root.GetProperty("extra").GetProperty("label").GetString(), Is.EqualTo("Speed"));
    }
}
