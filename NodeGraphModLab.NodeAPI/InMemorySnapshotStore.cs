using System.Collections.Concurrent;

namespace NodeGraphModLab.NodeAPI;

/// <summary>GetFirstHistoryForNode の返り値を格納するクラス。ValueTuple を避けることで Mono 互換性を確保する。</summary>
public sealed class PortHistoryResult
{
    public string PortName { get; }
    public List<SnapshotHistoryEntry> Entries { get; }
    public PortHistoryResult(string portName, List<SnapshotHistoryEntry> entries)
    {
        PortName = portName;
        Entries = entries;
    }
}

/// <summary>GetAllCurrentEntries の返り値を格納するクラス。ValueTuple を避けることで Mono 互換性を確保する。</summary>
public sealed class SnapshotEntryRecord
{
    public string NodeInstanceId { get; }
    public string PortName { get; }
    public object? Value { get; }
    public SnapshotEntryRecord(string nodeInstanceId, string portName, object? value)
    {
        NodeInstanceId = nodeInstanceId;
        PortName = portName;
        Value = value;
    }
}

/// <summary>
/// スナップショット履歴エントリ。
/// </summary>
public sealed class SnapshotHistoryEntry
{
    public object? Value { get; }
    public string ValueType { get; }
    public string? ValueString { get; }
    public DateTime Timestamp { get; }

    public SnapshotHistoryEntry(object? value, DateTime timestamp)
    {
        Value = value;
        ValueType = value?.GetType().Name ?? "null";
        ValueString = value?.ToString();
        Timestamp = timestamp;
    }
}

/// <summary>
/// インメモリ実装のスナップショットストア。
/// キーは "{nodeInstanceId}:{portName}" 形式。ホストプロセスの起動中のみ有効。
/// </summary>
public class InMemorySnapshotStore : ISnapshotStore
{
    private readonly ConcurrentDictionary<string, object?> _store = new();

    /// <summary>値の履歴（最大 MaxHistoryCount 件、新しい順）。キーは _store と同形式。</summary>
    private readonly ConcurrentDictionary<string, List<SnapshotHistoryEntry>> _history = new();
    private const int MaxHistoryCount = 10;

    /// <summary>SetSnapshot によって明示的に保存されたキーのセット。PushLive の履歴追記対象を絞るために使用。</summary>
    private readonly ConcurrentDictionary<string, byte> _snapshotKeys = new();

    private static string Key(string nodeInstanceId, string portName)
        => $"{nodeInstanceId}:{portName}";

    public virtual void SetSnapshot(string nodeInstanceId, string portName, object? value)
    {
        var key = Key(nodeInstanceId, portName);

        // 現在値が存在する場合は履歴に追加
        if (_store.TryGetValue(key, out var prev))
        {
            var entry = new SnapshotHistoryEntry(prev, DateTime.UtcNow);
            var list = _history.GetOrAdd(key, _ => new List<SnapshotHistoryEntry>());
            lock (list)
            {
                list.Insert(0, entry);
                if (list.Count > MaxHistoryCount) list.RemoveAt(list.Count - 1);
            }
        }

        _store[key] = value;
        _snapshotKeys.TryAdd(key, 0);
    }

    public object? GetSnapshot(string nodeInstanceId, string portName)
    {
        _store.TryGetValue(Key(nodeInstanceId, portName), out var value);
        return value;
    }

    public bool HasSnapshot(string nodeInstanceId, string portName)
        => _store.ContainsKey(Key(nodeInstanceId, portName));

    public void Clear()
    {
        _store.Clear();
        _history.Clear();
        _snapshotKeys.Clear();
    }

    /// <summary>デバッグ用: store/history の内容を文字列で返す。</summary>
    public string DebugDump()
    {
        var storeKeys = string.Join(", ", _store.Keys);
        var histInfo = string.Join(", ", _history.Keys.Select(k =>
        {
            _history.TryGetValue(k, out var l);
            return $"{k}({l?.Count ?? 0})";
        }));
        return $"Store=[{storeKeys}] History=[{histInfo}]";
    }

    /// <summary>
    /// 指定ポートの履歴を返す（新しい順、最大 MaxHistoryCount 件）。
    /// 現在値は含まない。
    /// </summary>
    public List<SnapshotHistoryEntry> GetHistory(string nodeInstanceId, string portName)
    {
        var key = Key(nodeInstanceId, portName);
        if (!_history.TryGetValue(key, out var list)) return new List<SnapshotHistoryEntry>();
        lock (list) return new List<SnapshotHistoryEntry>(list);
    }

    /// <summary>
    /// 指定ノードIDに属する最初の出力ポートの (portName, 履歴) を返す。
    /// ポート名が不明な場合に使用する。
    /// </summary>
    public PortHistoryResult? GetFirstHistoryForNode(string nodeInstanceId)
    {
        var prefix = $"{nodeInstanceId}:";
        // まず _store からポート名を取得
        var storeKey = _store.Keys.FirstOrDefault(k => k.StartsWith(prefix));
        if (storeKey != null)
        {
            var portName = storeKey.Substring(prefix.Length);
            return new PortHistoryResult(portName, GetHistory(nodeInstanceId, portName));
        }
        // store にない場合でも history にある可能性
        var histKey = _history.Keys.FirstOrDefault(k => k.StartsWith(prefix));
        if (histKey != null)
        {
            var portName = histKey.Substring(prefix.Length);
            return new PortHistoryResult(portName, GetHistory(nodeInstanceId, portName));
        }
        return null;
    }

    /// <summary>
    /// 指定インデックスの履歴値を現在値として復元する。
    /// </summary>
    public bool RestoreSnapshot(string nodeInstanceId, string portName, int historyIndex)
    {
        var key = Key(nodeInstanceId, portName);
        if (!_history.TryGetValue(key, out var list)) return false;
        SnapshotHistoryEntry? entry;
        lock (list)
        {
            if (historyIndex < 0 || historyIndex >= list.Count) return false;
            entry = list[historyIndex];
        }
        // 復元: 現在値を履歴に入れてから過去値をセット
        SetSnapshot(nodeInstanceId, portName, entry.Value);
        return true;
    }

    /// <summary>
    /// 指定ポートの現在値と履歴を削除する。
    /// </summary>
    public void ClearSnapshot(string nodeInstanceId, string portName)
    {
        var key = Key(nodeInstanceId, portName);
        _store.TryRemove(key, out _);
        _history.TryRemove(key, out _);
        _snapshotKeys.TryRemove(key, out _);
    }

    /// <summary>
    /// 指定ノードのすべてのポートの現在値と履歴を削除する。
    /// </summary>
    public void ClearAllSnapshotsForNode(string nodeInstanceId)
    {
        var prefix = $"{nodeInstanceId}:";
        foreach (var key in _store.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _store.TryRemove(key, out _);
        foreach (var key in _history.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _history.TryRemove(key, out _);
        foreach (var key in _snapshotKeys.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _snapshotKeys.TryRemove(key, out _);
    }

    /// <summary>
    /// 現在保存されている全スナップショットエントリを返す。
    /// 返すタプル: (nodeInstanceId, portName, value)
    /// </summary>
    public IReadOnlyList<SnapshotEntryRecord> GetAllCurrentEntries()
    {
        var result = new List<SnapshotEntryRecord>();
        foreach (var kv in _store)
        {
            var parts = kv.Key.Split(new char[] { ':' }, 2);
            if (parts.Length == 2)
                result.Add(new SnapshotEntryRecord(parts[0], parts[1], kv.Value));
        }
        return result;
    }

    /// <summary>
    /// グラフ実行外（永続コールバック等）からライブ値を書き込む。
    /// 値をストアに書き込む（通知は <see cref="NotifyingSnapshotStore"/> が担当）。
    /// </summary>
    public virtual void PushLive(string nodeInstanceId, string portName, object? value)
    {
        var key = Key(nodeInstanceId, portName);
        if (_snapshotKeys.ContainsKey(key) && _store.TryGetValue(key, out var prev))
        {
            var entry = new SnapshotHistoryEntry(prev, DateTime.UtcNow);
            var list = _history.GetOrAdd(key, _ => new List<SnapshotHistoryEntry>());
            lock (list)
            {
                list.Insert(0, entry);
                if (list.Count > MaxHistoryCount) list.RemoveAt(list.Count - 1);
            }
        }
        _store[key] = value;
    }

    /// <summary>
    /// 下流 Snapshot ノードへのライブ push 専用。
    /// SetSnapshot が未呼び出しでも履歴を蓄積する（_snapshotKeys チェックをスキップ）。
    /// 初回 push 時に _snapshotKeys へ自動登録し、以降の push でも履歴が積まれるようにする。
    /// </summary>
    public virtual void PushLiveToSnapshot(string nodeInstanceId, string portName, object? value)
    {
        var key = Key(nodeInstanceId, portName);

        // 前の値があれば無条件で履歴に追加（allowNull 不問）
        if (_store.TryGetValue(key, out var prev))
        {
            var entry = new SnapshotHistoryEntry(prev, DateTime.UtcNow);
            var list = _history.GetOrAdd(key, _ => new List<SnapshotHistoryEntry>());
            lock (list)
            {
                list.Insert(0, entry);
                if (list.Count > MaxHistoryCount) list.RemoveAt(list.Count - 1);
            }
        }

        _store[key] = value;
        // snapshotKeys に登録（以降 SetSnapshot からの履歴蓄積も有効になる）
        _snapshotKeys.TryAdd(key, 0);
    }
}
