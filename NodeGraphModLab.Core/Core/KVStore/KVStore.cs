using System.Collections.Concurrent;
using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Core.KVStore;

internal sealed class KVStore : IKVStore, IDisposable
{
    private readonly ConcurrentDictionary<string, object?> _cache = new();
    private readonly IKVStoreBackend _backend;

    public KVStore(IKVStoreBackend backend)
    {
        _backend = backend;
        foreach (var (key, json) in backend.LoadAll())
        {
            try { _cache[key] = DeserializeValue(json); }
            catch { /* 型解決失敗時はスキップ */ }
        }
    }

    public void Set(string key, object? value)
    {
        _cache[key] = value;
        _backend.Upsert(key, SerializeValue(value));
    }

    public object? Get(string key) { _cache.TryGetValue(key, out var v); return v; }

    public T? Get<T>(string key)
    {
        if (!_cache.TryGetValue(key, out var v)) return default;
        if (v is T typed) return typed;
        if (v is JsonElement je)
        {
            try { return JsonSerializer.Deserialize<T>(je.GetRawText()); } catch { }
        }
        try { return (T?)Convert.ChangeType(v, typeof(T)); } catch { return default; }
    }

    public bool TryGet<T>(string key, out T? value)
    {
        var exists = _cache.ContainsKey(key);
        value = Get<T>(key);
        return exists;
    }

    public bool ContainsKey(string key) => _cache.ContainsKey(key);

    public void Delete(string key)
    {
        _cache.TryRemove(key, out _);
        _backend.Delete(key);
    }

    public IEnumerable<string> Keys(string? prefix = null)
        => prefix == null
            ? _cache.Keys.ToList()
            : _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    public void Dispose() => _backend.Dispose();

    // 保存形式: "TypeFullName|JsonValue"
    // 型名に '|' が含まれないことを前提とする（System.* 型は安全）
    private static string SerializeValue(object? v)
    {
        var typeName = v?.GetType().FullName ?? "null";
        var json = JsonSerializer.Serialize(v);
        return typeName + "|" + json;
    }

    private static object? DeserializeValue(string stored)
    {
        var sep = stored.IndexOf('|');
        if (sep < 0) return stored;
        var typeName = stored.Substring(0, sep);
        var json = stored.Substring(sep + 1);
        if (typeName == "null") return null;
        var type = Type.GetType(typeName);
        // 型解決失敗時（IL2CPP / 未知型）は JsonElement で返す
        if (type == null) return JsonSerializer.Deserialize<JsonElement>(json);
        return JsonSerializer.Deserialize(json, type);
    }
}
