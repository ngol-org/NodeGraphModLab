using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NodeGraphModLab.Core.KVStore;

/// <summary>
/// 設定で指定された名前から永続化バックエンドを解決する。
///
/// 生成に失敗した場合は、外部ライブラリに依存しない実装へ段階的に退避する。
/// 明示的に指定された種類が使えなかったときに、外部ライブラリを使う実装へ戻すことはしない
/// （その依存を避けたくて明示指定しているのに、黙って戻っては意味がないため）。
/// </summary>
internal static class KVStoreBackendFactory
{
    public const string Auto = "auto";
    public const string LiteDb = "litedb";
    public const string JsonLines = "jsonl";
    public const string Memory = "memory";

    /// <summary>旧形式（キーと値の辞書1オブジェクト）。引き継ぎ元としてのみ指定できる。</summary>
    public const string LegacyJson = "json";

    /// <summary>設定に書ける値の一覧。検証とメッセージ生成に使う。</summary>
    public static readonly string[] KnownNames = { Auto, LiteDb, JsonLines, Memory };

    public static bool IsKnown(string? name)
        => name != null && Array.Exists(KnownNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// バックエンドを生成する。
    /// </summary>
    /// <param name="name">設定値。未知の値・空は <see cref="Auto"/> として扱う。</param>
    /// <param name="baseDir">保存ファイルを置くディレクトリ。</param>
    /// <param name="log">診断ログの出力先。</param>
    public static IKVStoreBackend Create(string? name, string baseDir, INgolLogger log)
        => Create(name, baseDir, log, out _);

    /// <summary>
    /// バックエンドを生成し、実際に選ばれた名前も返す。
    /// 退避が起きた場合は要求された名前と異なるため、記録や引き継ぎの判定にはこちらを使う。
    /// </summary>
    public static IKVStoreBackend Create(string? name, string baseDir, INgolLogger log, out string resolvedName)
    {
        var requested = string.IsNullOrWhiteSpace(name) ? Auto : name!.Trim().ToLowerInvariant();
        if (!IsKnown(requested))
        {
            log.LogWarning($"[KVStore] unknown backend '{name}', using '{Auto}' (valid: {string.Join(", ", KnownNames)})");
            requested = Auto;
        }

        var dbPath = Path.Combine(baseDir, "kvstore.db");
        var jsonlPath = Path.Combine(baseDir, "kvstore.jsonl");

        switch (requested)
        {
            case Memory:
                log.LogInfo("[KVStore] backend=memory (values are not persisted)");
                resolvedName = Memory;
                return new MemoryBackend();

            case JsonLines:
                return CreateJsonLinesOrMemory(jsonlPath, log, out resolvedName);

            case LiteDb:
                if (TryCreateLiteDb(dbPath, log, out var explicitLite)) { resolvedName = LiteDb; return explicitLite!; }
                // 明示指定でも litedb へは戻さず、外部依存のない側へ退避する。
                return CreateJsonLinesOrMemory(jsonlPath, log, out resolvedName);

            default: // Auto
                if (TryCreateLiteDb(dbPath, log, out var autoLite)) { resolvedName = LiteDb; return autoLite!; }
                return CreateJsonLinesOrMemory(jsonlPath, log, out resolvedName);
        }
    }

    private static IKVStoreBackend CreateJsonLinesOrMemory(string jsonlPath, INgolLogger log, out string resolvedName)
    {
        try
        {
            var backend = new JsonLinesBackend(jsonlPath, log);
            log.LogInfo($"[KVStore] backend=jsonl: {jsonlPath}");
            resolvedName = JsonLines;
            return backend;
        }
        catch (Exception ex)
        {
            log.LogError($"[KVStore] jsonl backend unavailable ({ex.Message}), falling back to memory (values are not persisted)");
            resolvedName = Memory;
            return new MemoryBackend();
        }
    }

    private static bool TryCreateLiteDb(string dbPath, INgolLogger log, out IKVStoreBackend? backend)
    {
        backend = null;
        try
        {
            backend = CreateLiteDbBackend(dbPath);
            log.LogInfo($"[KVStore] backend=litedb: {dbPath}");
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning($"[KVStore] litedb backend unavailable ({ex.GetType().Name}: {ex.Message})");
            return false;
        }
    }

    /// <summary>
    /// 保存先を切り替えた際に、以前の保存先から中身を一度だけ引き継ぐ。
    ///
    /// 値は解釈せず <see cref="IKVStoreBackend.LoadAll"/> が返した生のペアをそのまま書き写す。
    /// 復元と再シリアライズを挟むと型情報が落ちるため、ここでは中身に触れないことが重要。
    ///
    /// 実施済みかどうかは保存先ディレクトリの記録ファイルで判定する。
    /// ストア内に予約キーを置く方式は、利用者のキー空間を汚すうえ、
    /// 保存しない構成では記録自体が残らないため採らない。
    /// </summary>
    /// <param name="fromName">引き継ぎ元の名前。空なら何もしない。</param>
    /// <param name="toName">現在の保存先の名前（記録用）。</param>
    /// <param name="target">書き込み先。</param>
    public static void MigrateIfRequested(string? fromName, string toName, IKVStoreBackend target, string baseDir, INgolLogger log)
    {
        if (string.IsNullOrWhiteSpace(fromName)) return;

        var from = fromName!.Trim().ToLowerInvariant();
        var markerPath = Path.Combine(baseDir, "kvstore.migration.json");

        if (File.Exists(markerPath))
        {
            log.LogInfo($"[KVStore] migration from '{from}' already done (see {Path.GetFileName(markerPath)}), skipping");
            return;
        }

        if (string.Equals(from, toName, StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning($"[KVStore] migration source and destination are both '{from}', skipping");
            return;
        }

        IKVStoreBackend? source = null;
        try
        {
            source = CreateForMigration(from, baseDir, log);
            if (source == null)
            {
                log.LogWarning($"[KVStore] migration source '{from}' is not available, skipping");
                return;
            }

            var count = 0;
            foreach (var (key, valueJson) in source.LoadAll())
            {
                target.Upsert(key, valueJson);
                count++;
            }

            WriteMarker(markerPath, from, toName, count, log);
            log.LogInfo($"[KVStore] migrated {count} entries from '{from}' to '{toName}'");
        }
        catch (Exception ex)
        {
            // 記録ファイルを書かないので、次回起動でやり直しになる。
            // Upsert はキー単位で冪等なため、途中まで書き写した状態から再開しても結果は変わらない。
            log.LogError($"[KVStore] migration from '{from}' failed ({ex.Message}); it will be retried on next start");
        }
        finally
        {
            try { source?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// 引き継ぎ元を開く。書き込み先の生成と違い、失敗しても代替へ退避せず null を返す
    /// （元データが読めないのに別の空の保存先を読んでも意味がないため）。
    /// </summary>
    private static IKVStoreBackend? CreateForMigration(string name, string baseDir, INgolLogger log)
    {
        switch (name)
        {
            case LiteDb:
                return TryCreateLiteDb(Path.Combine(baseDir, "kvstore.db"), log, out var lite) ? lite : null;
            case JsonLines:
                return new JsonLinesBackend(Path.Combine(baseDir, "kvstore.jsonl"), log);
            case LegacyJson:
                // 旧形式は JsonLinesBackend が読み込み時に引き継ぐ経路を持っている。
                // ここでは .jsonl が無い状態で開くことで、その経路を通す。
                return new JsonLinesBackend(Path.Combine(baseDir, "kvstore.jsonl"), log);
            default:
                log.LogWarning($"[KVStore] unknown migration source '{name}' (valid: {LiteDb}, {JsonLines}, {LegacyJson})");
                return null;
        }
    }

    private static void WriteMarker(string path, string from, string to, int count, INgolLogger log)
    {
        try
        {
            var buffer = new MemoryStream(256);
            using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("from", from);
                w.WriteString("to", to);
                w.WriteString("utc", DateTime.UtcNow.ToString("o"));
                w.WriteNumber("entryCount", count);
                w.WriteEndObject();
            }
            File.WriteAllText(path, Encoding.UTF8.GetString(buffer.ToArray()), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            // 記録できないと次回も引き継ぎが走るが、冪等なので害はない。
            log.LogWarning($"[KVStore] failed to record migration ({ex.Message}); it may run again on next start");
        }
    }

    /// <summary>
    /// LiteDB の型に触れる処理をこのメソッドだけに閉じ込める。
    ///
    /// 型解決はメソッドの JIT 時に走るため、呼び出し側と同じメソッドに書くと
    /// 別のバックエンドを選んだ場合でも LiteDB アセンブリの解決が試みられてしまう。
    /// インライン化されると同じことが起きるので、明示的に抑止する。
    /// これにより、LiteDB を使わない構成では配布物からそのアセンブリを外せる。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IKVStoreBackend CreateLiteDbBackend(string dbPath) => new LiteDBBackend(dbPath);
}
