using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Server;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class WebUiPluginManifestTests
{
    private string _webUiDir = string.Empty;
    private string _pluginsDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _webUiDir = Path.Combine(Path.GetTempPath(), "ngol_test_webui_" + Guid.NewGuid().ToString("N"));
        _pluginsDir = Path.Combine(_webUiDir, "plugins");
        Directory.CreateDirectory(_pluginsDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_webUiDir)) Directory.Delete(_webUiDir, recursive: true);
    }

    [Test]
    public void BuildJson_NoPluginsDirectory_ReturnsEmptyArray()
    {
        Directory.Delete(_pluginsDir, recursive: true);

        Assert.That(WebUiPluginManifest.BuildJson(_webUiDir), Is.EqualTo("[]"));
    }

    [Test]
    public void BuildJson_EmptyPluginsDirectory_ReturnsEmptyArray()
    {
        Assert.That(WebUiPluginManifest.BuildJson(_webUiDir), Is.EqualTo("[]"));
    }

    [Test]
    public void BuildEntries_SingleJsFile_FormA()
    {
        File.WriteAllText(Path.Combine(_pluginsDir, "my-gauge.js"), "// plugin");

        var entries = WebUiPluginManifest.BuildEntries(_webUiDir);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Id, Is.EqualTo("my-gauge"));
        Assert.That(entries[0].ScriptUrl, Does.StartWith("/plugins/my-gauge.js?v="));
        Assert.That(entries[0].ApiVersion, Is.Null);
    }

    [Test]
    public void BuildEntries_FolderWithPluginJson_FormB()
    {
        var dir = Path.Combine(_pluginsDir, "gauge-pack");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"),
            "{\"id\":\"my.webui.gauge-pack\",\"version\":\"1.0.0\",\"displayName\":\"Gauge Pack\",\"scriptFile\":\"index.js\",\"apiVersion\":1}");
        File.WriteAllText(Path.Combine(dir, "index.js"), "// plugin");

        var entries = WebUiPluginManifest.BuildEntries(_webUiDir);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Id, Is.EqualTo("my.webui.gauge-pack"));
        Assert.That(entries[0].DisplayName, Is.EqualTo("Gauge Pack"));
        Assert.That(entries[0].Version, Is.EqualTo("1.0.0"));
        Assert.That(entries[0].ApiVersion, Is.EqualTo(1));
        Assert.That(entries[0].ScriptUrl, Does.StartWith("/plugins/gauge-pack/index.js?v="));
    }

    [Test]
    public void BuildEntries_MixedForms_ListsBoth()
    {
        File.WriteAllText(Path.Combine(_pluginsDir, "solo.js"), "// plugin");
        var dir = Path.Combine(_pluginsDir, "pack");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), "{\"id\":\"my.pack\"}");
        File.WriteAllText(Path.Combine(dir, "index.js"), "// plugin");

        var entries = WebUiPluginManifest.BuildEntries(_webUiDir);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries.Select(e => e.Id), Is.EquivalentTo(new[] { "solo", "my.pack" }));
    }

    [Test]
    public void BuildEntries_BrokenPluginJson_SkipsOnlyBrokenEntry()
    {
        var broken = Path.Combine(_pluginsDir, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "plugin.json"), "{ this is not json");
        File.WriteAllText(Path.Combine(broken, "index.js"), "// plugin");

        File.WriteAllText(Path.Combine(_pluginsDir, "healthy.js"), "// plugin");

        var entries = WebUiPluginManifest.BuildEntries(_webUiDir);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Id, Is.EqualTo("healthy"));
    }

    [Test]
    public void BuildEntries_MissingScriptFile_SkipsEntry()
    {
        var dir = Path.Combine(_pluginsDir, "no-script");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), "{\"id\":\"my.noscript\"}");

        Assert.That(WebUiPluginManifest.BuildEntries(_webUiDir), Is.Empty);
    }

    [Test]
    public void BuildEntries_ScriptFileWithPathTraversal_SkipsEntry()
    {
        var dir = Path.Combine(_pluginsDir, "evil");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"),
            "{\"id\":\"my.evil\",\"scriptFile\":\"../../index.html\"}");

        Assert.That(WebUiPluginManifest.BuildEntries(_webUiDir), Is.Empty);
    }

    [Test]
    public void BuildJson_ProducesValidJsonArray()
    {
        File.WriteAllText(Path.Combine(_pluginsDir, "a.js"), "// plugin");

        using var doc = JsonDocument.Parse(WebUiPluginManifest.BuildJson(_webUiDir));

        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(doc.RootElement.GetArrayLength(), Is.EqualTo(1));
        Assert.That(doc.RootElement[0].GetProperty("id").GetString(), Is.EqualTo("a"));
    }
}
