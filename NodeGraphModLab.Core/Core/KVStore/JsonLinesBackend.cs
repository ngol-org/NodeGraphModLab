using System.Text;
using System.Text.Json;

namespace NodeGraphModLab.Core.KVStore;

/// <summary>
/// 改行区切り JSON（1行1レコード）による永続化バックエンド。
///
/// KVStore 本体が全件をメモリに保持しており、バックエンドへランダム読み出しを要求しないため
/// （読み出しは起動時の <see cref="LoadAll"/> のみ）、追記型ログという単純な構造で成立する。
/// 更新は末尾への1行追記だけで済むため、大量の一括投入でも計算量が線形に収まる。
///
/// 追記だけではファイルが単調に増えるので、起動時の読み込み後に必要なら書き直して縮める。
///
/// 書き込みは <see cref="StreamWriter"/> のバッファに載せ、一定量たまるか一定時間経過するまで
/// ディスクへ流さない。プロセスが異常終了した場合に失われるのは直近の未フラッシュ分だけで、
/// それ以前の内容は残る。
/// </summary>
internal sealed class JsonLinesBackend : IKVStoreBackend
{
    /// <summary>この量を超えて書き込みが溜まったらフラッシュする。</summary>
    private const int FlushThresholdBytes = 64 * 1024;

    /// <summary>前回フラッシュからこの時間が経過していればフラッシュする。</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    /// <summary>総行数が有効キー数のこの倍数を超えたら、読み込み時に書き直して縮める。</summary>
    private const int CompactionLineRatio = 2;

    /// <summary>行数比が小さくてもこのサイズを超えていれば書き直しの対象にする。</summary>
    private const long CompactionSizeThresholdBytes = 8L * 1024 * 1024;

    private readonly string _path;
    private readonly INgolLogger? _log;
    private readonly object _lock = new();

    private StreamWriter? _writer;
    private int _pendingBytes;
    private DateTime _lastFlushUtc = DateTime.UtcNow;

    public JsonLinesBackend(string path, INgolLogger? log = null)
    {
        _path = path;
        _log = log;
    }

    public IEnumerable<(string Key, string ValueJson)> LoadAll()
    {
        lock (_lock)
        {
            // 書き込み用に開いたままだと読み直しと競合するため、一旦閉じる。
            CloseWriter();

            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            long totalLines = 0;

            if (!File.Exists(_path))
            {
                // 旧形式（キーと値の辞書1オブジェクト）が残っていれば一度だけ引き継ぐ。
                // 旧ファイルは残したままにする（読み込み側が消える不安を与えないため）。
                var legacy = Path.ChangeExtension(_path, ".json");
                if (TryLoadLegacy(legacy, entries))
                {
                    _log?.LogInfo($"[KVStore] migrated {entries.Count} entries from {Path.GetFileName(legacy)} to {Path.GetFileName(_path)}");
                    Rewrite(entries);
                }
                return entries.Select(kv => (kv.Key, kv.Value)).ToList();
            }

            long brokenLines = 0;
            foreach (var line in File.ReadLines(_path, Encoding.UTF8))
            {
                if (line.Length == 0) continue;
                totalLines++;

                if (!TryParseRecord(line, out var key, out var valueJson, out var deleted))
                {
                    // 異常終了で途中まで書かれた行が末尾に残ることがある。その行だけ捨てて続行する。
                    brokenLines++;
                    continue;
                }

                if (deleted) entries.Remove(key);
                else entries[key] = valueJson;
            }

            if (brokenLines > 0)
                _log?.LogWarning($"[KVStore] skipped {brokenLines} unreadable line(s) in {Path.GetFileName(_path)}");

            if (ShouldCompact(totalLines, entries.Count))
            {
                var before = SafeFileLength(_path);
                Rewrite(entries);
                var after = SafeFileLength(_path);
                _log?.LogInfo($"[KVStore] compacted {Path.GetFileName(_path)}: {totalLines} lines / {before / 1024} KB -> {entries.Count} lines / {after / 1024} KB");
            }

            return entries.Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }

    public void Upsert(string key, string valueJson)
    {
        lock (_lock)
        {
            WriteLine(BuildRecord(key, valueJson, deleted: false));
        }
    }

    public void Delete(string key)
    {
        lock (_lock)
        {
            WriteLine(BuildRecord(key, null, deleted: true));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            CloseWriter();
        }
    }

    // ---- 内部 ----

    private void WriteLine(string record)
    {
        EnsureWriter();
        _writer!.Write(record);
        _writer.Write('\n');

        _pendingBytes += record.Length + 1;
        var now = DateTime.UtcNow;
        if (_pendingBytes >= FlushThresholdBytes || now - _lastFlushUtc >= FlushInterval)
        {
            // フラッシュ失敗で書き込み自体を失敗させない。バッファは保持され次回に再試行される。
            try { _writer.Flush(); _pendingBytes = 0; _lastFlushUtc = now; }
            catch (Exception ex) { _log?.LogError($"[KVStore] flush failed: {ex.Message}"); }
        }
    }

    private void EnsureWriter()
    {
        if (_writer != null) return;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        _pendingBytes = 0;
        _lastFlushUtc = DateTime.UtcNow;
    }

    private void CloseWriter()
    {
        if (_writer == null) return;
        try { _writer.Flush(); }
        catch (Exception ex) { _log?.LogError($"[KVStore] flush on close failed: {ex.Message}"); }
        try { _writer.Dispose(); } catch { }
        _writer = null;
        _pendingBytes = 0;
    }

    /// <summary>現在の有効エントリだけを書き直して、追記で膨らんだ分を捨てる。</summary>
    private void Rewrite(Dictionary<string, string> entries)
    {
        CloseWriter();

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        using (var w = new StreamWriter(new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None), new UTF8Encoding(false)))
        {
            foreach (var kv in entries)
            {
                w.Write(BuildRecord(kv.Key, kv.Value, deleted: false));
                w.Write('\n');
            }
        }

        // 差し替えは原子的に行い、途中で落ちても元ファイルか新ファイルのどちらかが残るようにする。
        if (File.Exists(_path)) File.Replace(tmp, _path, null);
        else File.Move(tmp, _path);
    }

    private bool ShouldCompact(long totalLines, int liveCount)
    {
        if (totalLines <= liveCount) return false;
        if (SafeFileLength(_path) > CompactionSizeThresholdBytes) return true;
        return totalLines > (long)liveCount * CompactionLineRatio;
    }

    private static long SafeFileLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private bool TryLoadLegacy(string legacyPath, Dictionary<string, string> into)
    {
        if (!File.Exists(legacyPath)) return false;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(legacyPath));
            if (dict == null) return false;
            foreach (var kv in dict) into[kv.Key] = kv.Value;
            return into.Count > 0;
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[KVStore] failed to read {Path.GetFileName(legacyPath)}: {ex.Message}");
            return false;
        }
    }

    // レコードは System.Text.Json で組み立てる。キーや値に改行が含まれていても
    // エスケープされるため、1レコード=1行という前提が壊れない。
    private static string BuildRecord(string key, string? valueJson, bool deleted)
    {
        var buffer = new MemoryStream(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("k", key);
            if (deleted) w.WriteBoolean("d", true);
            else w.WriteString("v", valueJson ?? string.Empty);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static bool TryParseRecord(string line, out string key, out string valueJson, out bool deleted)
    {
        key = string.Empty;
        valueJson = string.Empty;
        deleted = false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("k", out var kEl) || kEl.ValueKind != JsonValueKind.String) return false;
            key = kEl.GetString() ?? string.Empty;

            if (root.TryGetProperty("d", out var dEl) && dEl.ValueKind == JsonValueKind.True)
            {
                deleted = true;
                return true;
            }

            if (!root.TryGetProperty("v", out var vEl) || vEl.ValueKind != JsonValueKind.String) return false;
            valueJson = vEl.GetString() ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
