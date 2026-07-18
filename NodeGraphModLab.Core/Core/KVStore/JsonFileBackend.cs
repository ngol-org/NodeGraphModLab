using System.Text.Json;

namespace NodeGraphModLab.Core.KVStore;

/// <summary>LiteDB 初期化失敗時のフォールバック、またはテスト用。</summary>
internal sealed class JsonFileBackend : IKVStoreBackend
{
    private readonly string _path;
    private readonly object _lock = new();

    public JsonFileBackend(string path) { _path = path; }

    public IEnumerable<(string, string)> LoadAll()
    {
        if (!File.Exists(_path)) return Enumerable.Empty<(string, string)>();
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path))
                   ?? new Dictionary<string, string>();
        return dict.Select(kv => (kv.Key, kv.Value));
    }

    public void Upsert(string key, string valueJson)
    {
        lock (_lock)
        {
            var dict = ReadDict();
            dict[key] = valueJson;
            WriteDict(dict);
        }
    }

    public void Delete(string key)
    {
        lock (_lock)
        {
            var dict = ReadDict();
            if (dict.Remove(key)) WriteDict(dict);
        }
    }

    public void Dispose() { }

    private Dictionary<string, string> ReadDict()
    {
        if (!File.Exists(_path)) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path))
               ?? new Dictionary<string, string>();
    }

    private void WriteDict(Dictionary<string, string> dict)
        => File.WriteAllText(_path, JsonSerializer.Serialize(dict));
}
