using NUnit.Framework;
using NodeGraphModLab;
using NodeGraphModLab.HostLogging;

namespace NodeGraphModLab.Tests;

/// <summary>
/// INgolLogger 実装（ConsoleFileNgolLogger / CompositeNgolLogger）の単体テスト。
/// </summary>
[TestFixture]
public class HostLoggingTests
{
    private sealed class RecordingLogger : INgolLogger
    {
        public List<(string Level, string Message)> Calls = new();
        public void LogInfo(string message)    => Calls.Add(("INFO", message));
        public void LogWarning(string message) => Calls.Add(("WARN", message));
        public void LogError(string message)   => Calls.Add(("ERROR", message));
        public void LogDebug(string message)   => Calls.Add(("DEBUG", message));
    }

    private sealed class ThrowingLogger : INgolLogger
    {
        public void LogInfo(string message)    => throw new InvalidOperationException("boom");
        public void LogWarning(string message) => throw new InvalidOperationException("boom");
        public void LogError(string message)   => throw new InvalidOperationException("boom");
        public void LogDebug(string message)   => throw new InvalidOperationException("boom");
    }

    [Test]
    public void CompositeNgolLogger_ForwardsToAllSinks()
    {
        var a = new RecordingLogger();
        var b = new RecordingLogger();
        var composite = new CompositeNgolLogger(a, b);

        composite.LogInfo("info-msg");
        composite.LogWarning("warn-msg");
        composite.LogError("error-msg");
        composite.LogDebug("debug-msg");

        Assert.That(a.Calls, Is.EqualTo(new List<(string, string)>
        {
            ("INFO", "info-msg"), ("WARN", "warn-msg"), ("ERROR", "error-msg"), ("DEBUG", "debug-msg")
        }));
        Assert.That(b.Calls, Is.EqualTo(a.Calls));
    }

    [Test]
    public void CompositeNgolLogger_OneSinkThrows_OthersStillReceiveLog()
    {
        var throwing = new ThrowingLogger();
        var recording = new RecordingLogger();
        var composite = new CompositeNgolLogger(throwing, recording);

        Assert.DoesNotThrow(() => composite.LogInfo("should-still-arrive"));
        Assert.That(recording.Calls, Has.Count.EqualTo(1));
        Assert.That(recording.Calls[0].Message, Is.EqualTo("should-still-arrive"));
    }

    [Test]
    public void ConsoleFileNgolLogger_WritesFormattedLinesToFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ngol-hostlogging-test-{Guid.NewGuid():N}.log");
        try
        {
            var logger = new ConsoleFileNgolLogger(path);
            logger.LogInfo("hello info");
            logger.LogWarning("hello warn");
            logger.LogError("hello error");
            logger.LogDebug("hello debug");

            var content = File.ReadAllText(path);

            Assert.That(content, Does.Contain("[INFO] hello info"));
            Assert.That(content, Does.Contain("[WARN] hello warn"));
            Assert.That(content, Does.Contain("[ERROR] hello error"));
            Assert.That(content, Does.Contain("[DEBUG] hello debug"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void ConsoleFileNgolLogger_TruncatesExistingFileOnConstruction()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ngol-hostlogging-test-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(path, "leftover from previous session\n");

            var logger = new ConsoleFileNgolLogger(path);
            logger.LogInfo("fresh session");

            var content = File.ReadAllText(path);

            Assert.That(content, Does.Not.Contain("leftover from previous session"));
            Assert.That(content, Does.Contain("fresh session"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void ConsoleFileNgolLogger_WritesNonAsciiContentAsUtf8()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ngol-hostlogging-test-{Guid.NewGuid():N}.log");
        try
        {
            var logger = new ConsoleFileNgolLogger(path);
            logger.LogInfo("非ASCII識別子を含む例外メッセージ");

            var content = File.ReadAllText(path, new System.Text.UTF8Encoding(false));

            Assert.That(content, Does.Contain("非ASCII識別子を含む例外メッセージ"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
