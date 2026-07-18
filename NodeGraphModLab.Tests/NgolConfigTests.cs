using System;
using System.IO;
using NUnit.Framework;
using NodeGraphModLab;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class NgolConfigTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ngol-config-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public void Load_NoFile_CreatesDefaultWithForceDirectModeFalse()
    {
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.Port, Is.EqualTo(NgolConfig.DefaultPort));
        Assert.That(NgolConfig.ForceDirectMode, Is.False);

        var written = File.ReadAllText(Path.Combine(_tempDir, "ngol-config.json"));
        Assert.That(written, Does.Contain("\"forceDirectMode\": false"));
        Assert.That(written, Does.Contain("\"requireAuthToken\": false"));
        Assert.That(NgolConfig.RequireAuthToken, Is.False);
    }

    [Test]
    public void Load_ForceDirectModeTrue_ParsesTrue()
    {
        WriteConfig("{ \"port\": 11156, \"forceDirectMode\": true }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.ForceDirectMode, Is.True);
    }

    [Test]
    public void Load_ForceDirectModeFalse_ParsesFalse()
    {
        WriteConfig("{ \"port\": 11156, \"forceDirectMode\": false }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.ForceDirectMode, Is.False);
    }

    [Test]
    public void Load_ForceDirectModeKeyOmitted_DefaultsFalse()
    {
        WriteConfig("{ \"port\": 11156 }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.ForceDirectMode, Is.False);
    }

    [Test]
    public void Load_NoFile_DefaultsDirectModeIntervalMs50WithoutWritingKey()
    {
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.DirectModeIntervalMs, Is.EqualTo(NgolConfig.DefaultDirectModeIntervalMs));

        // directModeIntervalMs は上級者向け設定のため自動生成ファイルには出力しない（手動追記のみ対応）
        var written = File.ReadAllText(Path.Combine(_tempDir, "ngol-config.json"));
        Assert.That(written, Does.Not.Contain("directModeIntervalMs"));
    }

    [Test]
    public void Load_DirectModeIntervalMsInRange_Parses()
    {
        WriteConfig("{ \"port\": 11156, \"directModeIntervalMs\": 200 }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.DirectModeIntervalMs, Is.EqualTo(200));
    }

    [Test]
    public void Load_DirectModeIntervalMsKeyOmitted_DefaultsTo50()
    {
        WriteConfig("{ \"port\": 11156 }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.DirectModeIntervalMs, Is.EqualTo(50));
    }

    [Test]
    public void Load_DirectModeIntervalMsBelowRange_FallsBackAndWarns()
    {
        WriteConfig("{ \"port\": 11156, \"directModeIntervalMs\": 0 }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.DirectModeIntervalMs, Is.EqualTo(50));
        Assert.That(logger.Warnings, Has.Some.Contains("directModeIntervalMs"));
    }

    [Test]
    public void Load_DirectModeIntervalMsAboveRange_FallsBackAndWarns()
    {
        WriteConfig("{ \"port\": 11156, \"directModeIntervalMs\": 99999 }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.DirectModeIntervalMs, Is.EqualTo(50));
        Assert.That(logger.Warnings, Has.Some.Contains("directModeIntervalMs"));
    }

    [Test]
    public void Load_InvalidJson_FallsBackToDefaultsAndWarns()
    {
        WriteConfig("{ \"port\": \"unterminated }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.Port, Is.EqualTo(NgolConfig.DefaultPort));
        Assert.That(NgolConfig.ForceDirectMode, Is.False);
        Assert.That(logger.Warnings, Has.Some.Contains("Failed to parse"));
    }

    [Test]
    public void Load_StartupSettingsKeyOmitted_DefaultsEmpty()
    {
        WriteConfig("{ \"port\": 11156 }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.StartupGraphId, Is.EqualTo(""));
        Assert.That(NgolConfig.StartupNodeTypeId, Is.EqualTo(""));
        Assert.That(NgolConfig.StartupNodeInputsJson, Is.Null);
    }

    [Test]
    public void Load_StartupSettings_Parses()
    {
        WriteConfig("""
            {
              "port": 11156,
              "startupGraphId": "my_graph",
              "startupNodeTypeId": "ngol.logic.log",
              "startupNodeInputs": { "message": "hello" }
            }
            """);
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.StartupGraphId, Is.EqualTo("my_graph"));
        Assert.That(NgolConfig.StartupNodeTypeId, Is.EqualTo("ngol.logic.log"));
        Assert.That(NgolConfig.StartupNodeInputsJson, Does.Contain("hello"));
    }

    [Test]
    public void Load_NoFile_DefaultStartupSettingsNotWritten()
    {
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        var written = File.ReadAllText(Path.Combine(_tempDir, "ngol-config.json"));
        Assert.That(written, Does.Not.Contain("startupGraphId"));
        Assert.That(written, Does.Not.Contain("startupNodeTypeId"));
        Assert.That(written, Does.Not.Contain("startupNodeInputs"));
    }

    [Test]
    public void Load_CustomNodeDirectoriesKeyOmitted_DefaultsEmpty()
    {
        WriteConfig("{ \"port\": 11156 }");
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.CustomNodeDirectories, Is.Empty);
    }

    [Test]
    public void Load_CustomNodeDirectories_ParsesInOrder()
    {
        WriteConfig("""
            {
              "port": 11156,
              "customNodeDirectories": ["D:\\Shared\\NodesA", "D:\\Shared\\NodesB"]
            }
            """);
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.CustomNodeDirectories, Is.EqualTo(new[] { "D:\\Shared\\NodesA", "D:\\Shared\\NodesB" }));
    }

    [Test]
    public void Load_CustomNodeDirectoriesContainsEmptyEntry_SkipsAndWarns()
    {
        WriteConfig("""
            {
              "port": 11156,
              "customNodeDirectories": ["D:\\Shared\\NodesA", "", "   "]
            }
            """);
        var logger = new RecordingLogger();

        NgolConfig.Load(_tempDir, logger);

        Assert.That(NgolConfig.CustomNodeDirectories, Is.EqualTo(new[] { "D:\\Shared\\NodesA" }));
        Assert.That(logger.Warnings, Has.Some.Contains("customNodeDirectories"));
    }

    private void WriteConfig(string json)
    {
        File.WriteAllText(Path.Combine(_tempDir, "ngol-config.json"), json, System.Text.Encoding.UTF8);
    }

    private sealed class RecordingLogger : INgolLogger
    {
        public System.Collections.Generic.List<string> Warnings { get; } = new();

        public void LogInfo(string message) { }
        public void LogWarning(string message) => Warnings.Add(message);
        public void LogError(string message) { }
        public void LogDebug(string message) { }
    }
}
