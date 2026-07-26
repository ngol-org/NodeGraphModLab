using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using NodeGraphModLab.Core.KVStore;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class JsonLinesBackendTests
{
    private string _dir = null!;
    private string _path = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ngol_jsonl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "kvstore.jsonl");
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private Dictionary<string, string> LoadIntoDict()
    {
        using var b = new JsonLinesBackend(_path);
        return b.LoadAll().ToDictionary(x => x.Key, x => x.ValueJson);
    }

    [Test]
    public void LoadAll_NoFile_ReturnsEmpty()
    {
        Assert.That(LoadIntoDict(), Is.Empty);
    }

    [Test]
    public void UpsertThenLoad_RoundTrips()
    {
        using (var b = new JsonLinesBackend(_path))
        {
            b.Upsert("a", "System.Int32|1");
            b.Upsert("b", "System.String|\"x\"");
        }

        var loaded = LoadIntoDict();

        Assert.That(loaded, Has.Count.EqualTo(2));
        Assert.That(loaded["a"], Is.EqualTo("System.Int32|1"));
        Assert.That(loaded["b"], Is.EqualTo("System.String|\"x\""));
    }

    [Test]
    public void Upsert_SameKeyTwice_LastValueWins()
    {
        using (var b = new JsonLinesBackend(_path))
        {
            b.Upsert("a", "v1");
            b.Upsert("a", "v2");
        }

        Assert.That(LoadIntoDict()["a"], Is.EqualTo("v2"));
    }

    [Test]
    public void Delete_RemovesEntryOnReplay()
    {
        using (var b = new JsonLinesBackend(_path))
        {
            b.Upsert("a", "v1");
            b.Upsert("b", "v2");
            b.Delete("a");
        }

        var loaded = LoadIntoDict();

        Assert.That(loaded.ContainsKey("a"), Is.False);
        Assert.That(loaded["b"], Is.EqualTo("v2"));
    }

    [Test]
    public void Delete_ThenUpsertAgain_EntryComesBack()
    {
        using (var b = new JsonLinesBackend(_path))
        {
            b.Upsert("a", "v1");
            b.Delete("a");
            b.Upsert("a", "v2");
        }

        Assert.That(LoadIntoDict()["a"], Is.EqualTo("v2"));
    }

    [Test]
    public void LoadAll_TruncatedLastLine_IsSkippedAndRestSurvives()
    {
        using (var b = new JsonLinesBackend(_path))
        {
            b.Upsert("a", "v1");
            b.Upsert("b", "v2");
        }
        // 異常終了で途中まで書かれた行を模す
        File.AppendAllText(_path, "{\"k\":\"c\",\"v\":\"v3", Encoding.UTF8);

        var loaded = LoadIntoDict();

        Assert.That(loaded, Has.Count.EqualTo(2));
        Assert.That(loaded.ContainsKey("c"), Is.False);
    }

    [Test]
    public void KeyAndValueWithNewlines_SurviveRoundTrip()
    {
        // 1レコード=1行という前提が壊れないことの確認
        const string key = "line1\nline2";
        const string value = "System.String|\"a\\nb\"\nnot-a-record";

        using (var b = new JsonLinesBackend(_path))
        {
            b.Upsert(key, value);
        }

        var loaded = LoadIntoDict();

        Assert.That(loaded, Has.Count.EqualTo(1));
        Assert.That(loaded[key], Is.EqualTo(value));
    }

    [Test]
    public void LoadAll_ManyRewrites_CompactsFileAndKeepsData()
    {
        using (var b = new JsonLinesBackend(_path))
        {
            for (int i = 0; i < 200; i++) b.Upsert("k", "v" + i);
        }
        var linesBefore = File.ReadAllLines(_path).Length;

        var loaded = LoadIntoDict();          // ここでコンパクションが走る
        var linesAfter = File.ReadAllLines(_path).Length;

        Assert.That(linesBefore, Is.EqualTo(200));
        Assert.That(linesAfter, Is.EqualTo(1), "有効キーは1件なので1行まで縮むはず");
        Assert.That(loaded["k"], Is.EqualTo("v199"));
    }

    [Test]
    public void LoadAll_AfterCompaction_AppendsStillWork()
    {
        using (var b = new JsonLinesBackend(_path))
        {
            for (int i = 0; i < 50; i++) b.Upsert("k", "v" + i);
        }

        using (var b = new JsonLinesBackend(_path))
        {
            b.LoadAll().ToList();             // コンパクション
            b.Upsert("k2", "after");          // 直後の追記が壊れないこと
        }

        var loaded = LoadIntoDict();
        Assert.That(loaded["k"], Is.EqualTo("v49"));
        Assert.That(loaded["k2"], Is.EqualTo("after"));
    }

    [Test]
    public void LoadAll_LegacyJsonPresentAndNoJsonl_MigratesOnce()
    {
        var legacy = Path.ChangeExtension(_path, ".json");
        File.WriteAllText(legacy, "{\"a\":\"v1\",\"b\":\"v2\"}", Encoding.UTF8);

        var loaded = LoadIntoDict();

        Assert.That(loaded, Has.Count.EqualTo(2));
        Assert.That(loaded["a"], Is.EqualTo("v1"));
        Assert.That(File.Exists(_path), Is.True, "jsonl へ書き出されるはず");
        Assert.That(File.Exists(legacy), Is.True, "旧ファイルは残す");
    }

    [Test]
    public void LoadAll_JsonlExists_LegacyJsonIsIgnored()
    {
        var legacy = Path.ChangeExtension(_path, ".json");
        File.WriteAllText(legacy, "{\"old\":\"should-not-load\"}", Encoding.UTF8);
        using (var b = new JsonLinesBackend(_path)) b.Upsert("new", "v");

        var loaded = LoadIntoDict();

        Assert.That(loaded.ContainsKey("old"), Is.False);
        Assert.That(loaded["new"], Is.EqualTo("v"));
    }

    [Test]
    public void Upsert_FromMultipleThreads_AllEntriesPersisted()
    {
        const int threads = 8;
        const int perThread = 250;

        using (var b = new JsonLinesBackend(_path))
        {
            Parallel.For(0, threads, t =>
            {
                for (int i = 0; i < perThread; i++) b.Upsert($"t{t}-{i}", "v");
            });
        }

        Assert.That(LoadIntoDict(), Has.Count.EqualTo(threads * perThread));
    }

    // ---- ファクトリ ----

    private sealed class CollectingLogger : NodeGraphModLab.INgolLogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public void LogInfo(string message) => Infos.Add(message);
        public void LogWarning(string message) => Warnings.Add(message);
        public void LogError(string message) => Errors.Add(message);
        public void LogDebug(string message) { }
    }

    [Test]
    public void Factory_Memory_ReturnsNonPersistingBackend()
    {
        var log = new CollectingLogger();

        using var b = KVStoreBackendFactory.Create("memory", _dir, log);
        b.Upsert("a", "v");

        Assert.That(b, Is.TypeOf<MemoryBackend>());
        Assert.That(b.LoadAll(), Is.Empty);
        Assert.That(Directory.GetFiles(_dir), Is.Empty, "ファイルを作らないこと");
    }

    [Test]
    public void Factory_JsonLines_WritesJsonlFile()
    {
        var log = new CollectingLogger();

        using (var b = KVStoreBackendFactory.Create("jsonl", _dir, log))
        {
            Assert.That(b, Is.TypeOf<JsonLinesBackend>());
            b.Upsert("a", "v");
        }

        Assert.That(File.Exists(Path.Combine(_dir, "kvstore.jsonl")), Is.True);
        Assert.That(File.Exists(Path.Combine(_dir, "kvstore.db")), Is.False, "選ばれていない実装のファイルを作らないこと");
    }

    [Test]
    public void Factory_NameIsCaseInsensitiveAndTrimmed()
    {
        var log = new CollectingLogger();

        using var b = KVStoreBackendFactory.Create("  MEMORY  ", _dir, log);

        Assert.That(b, Is.TypeOf<MemoryBackend>());
        Assert.That(log.Warnings, Is.Empty);
    }

    [Test]
    public void Factory_UnknownName_WarnsAndFallsBackToAuto()
    {
        var log = new CollectingLogger();

        using var b = KVStoreBackendFactory.Create("sqlite", _dir, log);

        Assert.That(log.Warnings.Any(w => w.Contains("unknown backend")), Is.True);
        // auto は litedb を試し、失敗すれば jsonl。どちらでも memory ではない。
        Assert.That(b, Is.Not.TypeOf<MemoryBackend>());
    }

    [Test]
    public void Factory_NullOrEmpty_TreatedAsAuto()
    {
        var log = new CollectingLogger();

        using var b1 = KVStoreBackendFactory.Create(null, _dir, log);
        using var b2 = KVStoreBackendFactory.Create("   ", _dir, log);

        Assert.That(log.Warnings.Any(w => w.Contains("unknown backend")), Is.False);
        Assert.That(b1, Is.Not.TypeOf<MemoryBackend>());
        Assert.That(b2, Is.Not.TypeOf<MemoryBackend>());
    }

    // ---- 引き継ぎ（移行） ----

    private string MarkerPath => Path.Combine(_dir, "kvstore.migration.json");

    [Test]
    public void Migrate_NotRequested_DoesNothing()
    {
        var log = new CollectingLogger();
        using var target = new JsonLinesBackend(_path);

        KVStoreBackendFactory.MigrateIfRequested("", "jsonl", target, _dir, log);
        KVStoreBackendFactory.MigrateIfRequested(null, "jsonl", target, _dir, log);

        Assert.That(File.Exists(MarkerPath), Is.False);
    }

    [Test]
    public void Migrate_FromLegacyJson_CopiesRawPairsAndWritesMarker()
    {
        File.WriteAllText(Path.ChangeExtension(_path, ".json"),
            "{\"a\":\"System.Int32|1\",\"b\":\"System.String|\\\"x\\\"\"}", Encoding.UTF8);

        var log = new CollectingLogger();
        var targetPath = Path.Combine(_dir, "target.jsonl");
        using (var target = new JsonLinesBackend(targetPath))
        {
            KVStoreBackendFactory.MigrateIfRequested("json", "jsonl", target, _dir, log);
        }

        using var reader = new JsonLinesBackend(targetPath);
        var loaded = reader.LoadAll().ToDictionary(x => x.Key, x => x.ValueJson);

        // 値は再解釈せずそのまま書き写されること（型情報が落ちない）
        Assert.That(loaded["a"], Is.EqualTo("System.Int32|1"));
        Assert.That(loaded["b"], Is.EqualTo("System.String|\"x\""));
        Assert.That(File.Exists(MarkerPath), Is.True);
        Assert.That(File.ReadAllText(MarkerPath), Does.Contain("\"entryCount\": 2"));
    }

    [Test]
    public void Migrate_MarkerExists_IsSkipped()
    {
        File.WriteAllText(Path.ChangeExtension(_path, ".json"), "{\"a\":\"v\"}", Encoding.UTF8);
        File.WriteAllText(MarkerPath, "{}", Encoding.UTF8);

        var log = new CollectingLogger();
        var targetPath = Path.Combine(_dir, "target.jsonl");
        using (var target = new JsonLinesBackend(targetPath))
        {
            KVStoreBackendFactory.MigrateIfRequested("json", "jsonl", target, _dir, log);
        }

        Assert.That(File.Exists(targetPath), Is.False, "スキップされるので書き込みが起きないこと");
        Assert.That(log.Infos.Any(i => i.Contains("already done")), Is.True);
    }

    [Test]
    public void Migrate_SameSourceAndDestination_IsSkippedWithWarning()
    {
        var log = new CollectingLogger();
        using var target = new JsonLinesBackend(_path);

        KVStoreBackendFactory.MigrateIfRequested("jsonl", "jsonl", target, _dir, log);

        Assert.That(log.Warnings.Any(w => w.Contains("both")), Is.True);
        Assert.That(File.Exists(MarkerPath), Is.False);
    }

    [Test]
    public void Migrate_UnknownSource_WarnsAndDoesNotWriteMarker()
    {
        var log = new CollectingLogger();
        using var target = new JsonLinesBackend(_path);

        KVStoreBackendFactory.MigrateIfRequested("sqlite", "jsonl", target, _dir, log);

        Assert.That(log.Warnings.Any(w => w.Contains("unknown migration source")), Is.True);
        Assert.That(File.Exists(MarkerPath), Is.False);
    }

    [Test]
    public void Migrate_RunTwice_SecondRunIsSkipped()
    {
        File.WriteAllText(Path.ChangeExtension(_path, ".json"), "{\"a\":\"v1\"}", Encoding.UTF8);
        var targetPath = Path.Combine(_dir, "target.jsonl");
        var log = new CollectingLogger();

        using (var t1 = new JsonLinesBackend(targetPath))
            KVStoreBackendFactory.MigrateIfRequested("json", "jsonl", t1, _dir, log);
        using (var t2 = new JsonLinesBackend(targetPath))
            KVStoreBackendFactory.MigrateIfRequested("json", "jsonl", t2, _dir, log);

        using var reader = new JsonLinesBackend(targetPath);
        Assert.That(reader.LoadAll().Count(), Is.EqualTo(1));
        Assert.That(log.Infos.Count(i => i.Contains("already done")), Is.EqualTo(1));
    }

    [Test]
    public void Factory_IsKnown_MatchesKnownNames()
    {
        foreach (var n in KVStoreBackendFactory.KnownNames)
        {
            Assert.That(KVStoreBackendFactory.IsKnown(n), Is.True, n);
            Assert.That(KVStoreBackendFactory.IsKnown(n.ToUpperInvariant()), Is.True, n);
        }
        Assert.That(KVStoreBackendFactory.IsKnown("sqlite"), Is.False);
        Assert.That(KVStoreBackendFactory.IsKnown(null), Is.False);
    }
}
