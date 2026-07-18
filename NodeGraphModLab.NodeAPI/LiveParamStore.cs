using System.Collections.Concurrent;

namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// スレッドセーフなライブパラメータストア実装。
/// </summary>
public sealed class LiveParamStore : ILiveParamStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object?>> _byNode = new();

    public IReadOnlyList<string> MergeParams(string nodeInstanceId, IReadOnlyDictionary<string, object?> parameters)
    {
        if (string.IsNullOrEmpty(nodeInstanceId) || parameters.Count == 0)
            return Array.Empty<string>();

        var nodeParams = _byNode.GetOrAdd(nodeInstanceId, _ => new ConcurrentDictionary<string, object?>());
        var merged = new List<string>(parameters.Count);
        foreach (var kv in parameters)
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            nodeParams[kv.Key] = kv.Value;
            merged.Add(kv.Key);
        }
        return merged;
    }

    public bool TryGet(string nodeInstanceId, string key, out object? value)
    {
        value = null;
        if (string.IsNullOrEmpty(nodeInstanceId) || string.IsNullOrEmpty(key)) return false;
        return _byNode.TryGetValue(nodeInstanceId, out var nodeParams)
               && nodeParams.TryGetValue(key, out value);
    }

    public void ClearNode(string nodeInstanceId)
    {
        if (string.IsNullOrEmpty(nodeInstanceId)) return;
        _byNode.TryRemove(nodeInstanceId, out _);
    }

    public void ClearAll() => _byNode.Clear();

    /// <summary>ライブパラメータ値を T に変換する。変換不能時は defaultValue を返す。</summary>
    public static T ConvertValue<T>(object? raw, T defaultValue)
    {
        if (raw == null) return defaultValue;
        if (raw is T typed) return typed;

        try
        {
            if (raw is double d)
            {
                if (typeof(T) == typeof(float)) return (T)(object)(float)d;
                if (typeof(T) == typeof(int)) return (T)(object)(int)d;
                if (typeof(T) == typeof(long)) return (T)(object)(long)d;
                if (typeof(T) == typeof(bool)) return (T)(object)(d != 0);
            }
            if (raw is string s)
            {
                if (typeof(T) == typeof(bool) && bool.TryParse(s, out var b)) return (T)(object)b;
                if (typeof(T) == typeof(double) && double.TryParse(s, out var dv)) return (T)(object)dv;
                if (typeof(T) == typeof(float) && float.TryParse(s, out var fv)) return (T)(object)fv;
                if (typeof(T) == typeof(int) && int.TryParse(s, out var iv)) return (T)(object)iv;
            }
            return (T)Convert.ChangeType(raw, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}
