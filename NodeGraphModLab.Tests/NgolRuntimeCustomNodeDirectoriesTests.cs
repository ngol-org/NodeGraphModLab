using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using NodeGraphModLab;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class NgolRuntimeCustomNodeDirectoriesTests
{
    private string _tempDir = null!;
    private string _primaryDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ngol-runtime-customdirs-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _primaryDir = Path.Combine(_tempDir, "primary");
        Directory.CreateDirectory(_primaryDir);
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
    public void Resolve_EmptyConfigured_ReturnsEmpty()
    {
        var logger = new RecordingLogger();

        var result = NgolRuntime.ResolveCustomNodeDirectories(Array.Empty<string>(), _primaryDir, logger);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Resolve_ExistingExtraDirectory_IsIncluded()
    {
        var extra = Path.Combine(_tempDir, "extra");
        Directory.CreateDirectory(extra);
        var logger = new RecordingLogger();

        var result = NgolRuntime.ResolveCustomNodeDirectories(new[] { extra }, _primaryDir, logger);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(Path.GetFullPath(result[0]).TrimEnd(Path.DirectorySeparatorChar),
            Is.EqualTo(Path.GetFullPath(extra).TrimEnd(Path.DirectorySeparatorChar)));
    }

    [Test]
    public void Resolve_NonExistentDirectory_SkippedWithWarning()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");
        var logger = new RecordingLogger();

        var result = NgolRuntime.ResolveCustomNodeDirectories(new[] { missing }, _primaryDir, logger);

        Assert.That(result, Is.Empty);
        Assert.That(logger.Warnings, Has.Some.Contains("not found"));
    }

    [Test]
    public void Resolve_SameAsPrimaryDirectory_ExcludedWithoutWarning()
    {
        var logger = new RecordingLogger();

        var result = NgolRuntime.ResolveCustomNodeDirectories(new[] { _primaryDir }, _primaryDir, logger);

        Assert.That(result, Is.Empty);
        Assert.That(logger.Warnings, Is.Empty);
    }

    [Test]
    public void Resolve_DuplicateEntriesDifferingByCaseAndTrailingSlash_Deduplicated()
    {
        var extra = Path.Combine(_tempDir, "extra");
        Directory.CreateDirectory(extra);
        var logger = new RecordingLogger();

        var variants = new[]
        {
            extra,
            extra.ToUpperInvariant(),
            extra + Path.DirectorySeparatorChar
        };

        var result = NgolRuntime.ResolveCustomNodeDirectories(variants, _primaryDir, logger);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void Resolve_BlankEntries_Skipped()
    {
        var logger = new RecordingLogger();

        var result = NgolRuntime.ResolveCustomNodeDirectories(new[] { "", "   " }, _primaryDir, logger);

        Assert.That(result, Is.Empty);
    }

    private sealed class RecordingLogger : INgolLogger
    {
        public List<string> Warnings { get; } = new();

        public void LogInfo(string message) { }
        public void LogWarning(string message) => Warnings.Add(message);
        public void LogError(string message) { }
        public void LogDebug(string message) { }
    }
}
