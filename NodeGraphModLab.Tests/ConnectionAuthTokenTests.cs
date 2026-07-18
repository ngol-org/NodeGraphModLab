using System;
using System.IO;
using NUnit.Framework;
using NodeGraphModLab;
using NodeGraphModLab.Server;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class ConnectionAuthTokenTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ngol-auth-token-tests-" + Guid.NewGuid());
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
    public void Initialize_Disabled_CurrentTokenNullAndValidateAlwaysTrue()
    {
        var logger = new RecordingLogger();

        ConnectionAuthToken.Initialize(_tempDir, enabled: false, port: 11156, logger);

        Assert.That(ConnectionAuthToken.CurrentToken, Is.Null);
        Assert.That(ConnectionAuthToken.Validate(null), Is.True);
        Assert.That(ConnectionAuthToken.Validate("anything"), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempDir, ConnectionAuthToken.TokenFileName)), Is.False);
    }

    [Test]
    public void Initialize_EnabledNoExistingFile_GeneratesAndPersistsToken()
    {
        var logger = new RecordingLogger();

        ConnectionAuthToken.Initialize(_tempDir, enabled: true, port: 11156, logger);

        Assert.That(ConnectionAuthToken.CurrentToken, Is.Not.Null.And.Not.Empty);
        var path = Path.Combine(_tempDir, ConnectionAuthToken.TokenFileName);
        Assert.That(File.Exists(path), Is.True);
        Assert.That(File.ReadAllText(path).Trim(), Is.EqualTo(ConnectionAuthToken.CurrentToken));

        // Base64Url のみ（+ / = を含まない）であること
        Assert.That(ConnectionAuthToken.CurrentToken, Does.Not.Contain("+"));
        Assert.That(ConnectionAuthToken.CurrentToken, Does.Not.Contain("/"));
        Assert.That(ConnectionAuthToken.CurrentToken, Does.Not.Contain("="));
    }

    [Test]
    public void Initialize_EnabledWithExistingFile_ReusesSameToken()
    {
        var logger = new RecordingLogger();
        var path = Path.Combine(_tempDir, ConnectionAuthToken.TokenFileName);
        File.WriteAllText(path, "existing-token-value");

        ConnectionAuthToken.Initialize(_tempDir, enabled: true, port: 11156, logger);

        Assert.That(ConnectionAuthToken.CurrentToken, Is.EqualTo("existing-token-value"));
    }

    [Test]
    public void Initialize_DisableThenReenable_ReusesTokenFileLeftOnDisk()
    {
        var logger = new RecordingLogger();

        ConnectionAuthToken.Initialize(_tempDir, enabled: true, port: 11156, logger);
        var firstToken = ConnectionAuthToken.CurrentToken;

        ConnectionAuthToken.Initialize(_tempDir, enabled: false, port: 11156, logger);
        Assert.That(ConnectionAuthToken.CurrentToken, Is.Null);

        ConnectionAuthToken.Initialize(_tempDir, enabled: true, port: 11156, logger);
        Assert.That(ConnectionAuthToken.CurrentToken, Is.EqualTo(firstToken));
    }

    [Test]
    public void Validate_EnabledCorrectToken_ReturnsTrue()
    {
        var logger = new RecordingLogger();
        ConnectionAuthToken.Initialize(_tempDir, enabled: true, port: 11156, logger);

        Assert.That(ConnectionAuthToken.Validate(ConnectionAuthToken.CurrentToken), Is.True);
    }

    [Test]
    public void Validate_EnabledWrongOrMissingToken_ReturnsFalse()
    {
        var logger = new RecordingLogger();
        ConnectionAuthToken.Initialize(_tempDir, enabled: true, port: 11156, logger);

        Assert.That(ConnectionAuthToken.Validate("wrong-token"), Is.False);
        Assert.That(ConnectionAuthToken.Validate(null), Is.False);
    }

    private sealed class RecordingLogger : INgolLogger
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
        public void LogDebug(string message) { }
    }
}
