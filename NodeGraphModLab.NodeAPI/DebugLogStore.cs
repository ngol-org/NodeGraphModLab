using System.Text.Json;
using System.Text.RegularExpressions;

namespace NodeGraphModLab.NodeAPI;

public sealed class DebugLogEntry
{
    public string Kind { get; }
    public string Level { get; }
    public string Message { get; }
    public DateTime Timestamp { get; }

    public DebugLogEntry(string kind, string level, string message, DateTime timestamp)
    {
        Kind = kind;
        Level = level;
        Message = message;
        Timestamp = timestamp;
    }
}

/// <summary>
/// GetFiltered の絞り込み条件。null のフィールドは条件なし（全件一致）。
/// </summary>
public sealed class DebugLogFilter
{
    private static readonly string[] LevelOrder = { "log", "warn", "error" };
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public string? Kind { get; set; }
    public string? Level { get; set; }
    public string? MessageContains { get; set; }
    public string? DomEventType { get; set; }
    public long? SinceMs { get; set; }
    public long? UntilMs { get; set; }
    public string? LevelAtLeast { get; set; }
    public string? MessageRegex { get; set; }

    public bool Matches(DebugLogEntry entry)
    {
        if (Kind != null && !string.Equals(entry.Kind, Kind, StringComparison.OrdinalIgnoreCase)) return false;
        if (Level != null && !string.Equals(entry.Level, Level, StringComparison.OrdinalIgnoreCase)) return false;
        if (MessageContains != null &&
            entry.Message.IndexOf(MessageContains, StringComparison.OrdinalIgnoreCase) < 0) return false;

        if (LevelAtLeast != null)
        {
            var entryIdx = Array.IndexOf(LevelOrder, entry.Level.ToLowerInvariant());
            var thresholdIdx = Array.IndexOf(LevelOrder, LevelAtLeast.ToLowerInvariant());
            if (entryIdx < 0 || thresholdIdx < 0 || entryIdx < thresholdIdx) return false;
        }

        if (SinceMs != null || UntilMs != null)
        {
            var ms = new DateTimeOffset(entry.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (SinceMs != null && ms < SinceMs) return false;
            if (UntilMs != null && ms > UntilMs) return false;
        }

        if (DomEventType != null)
        {
            if (!string.Equals(entry.Kind, "dom_event", StringComparison.OrdinalIgnoreCase)) return false;
            if (!TryGetJsonStringField(entry.Message, "type", out var type) ||
                !string.Equals(type, DomEventType, StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (MessageRegex != null && !SafeRegexIsMatch(entry.Message, MessageRegex)) return false;

        return true;
    }

    private static bool TryGetJsonStringField(string json, string propertyName, out string? value)
    {
        value = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                value = prop.GetString();
                return true;
            }
        }
        catch (JsonException)
        {
            // dom_event 以外の非JSON message等、パース失敗時は非該当として扱う
        }
        return false;
    }

    private static bool SafeRegexIsMatch(string input, string pattern)
    {
        try
        {
            // AIエージェントから任意パターンを受け取るため matchTimeout 必須（ReDoS対策）
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

public interface IDebugLogStore
{
    void Add(string kind, string level, string message);
    List<DebugLogEntry> GetRecent(int count);
    List<DebugLogEntry> GetFiltered(DebugLogFilter filter, int count);
    void Clear();
}

public sealed class DebugLogStore : IDebugLogStore
{
    private readonly List<DebugLogEntry> _entries = new();
    private const int MaxCount = 200;

    public void Add(string kind, string level, string message)
    {
        var entry = new DebugLogEntry(kind, level, message, DateTime.UtcNow);
        lock (_entries)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxCount) _entries.RemoveAt(_entries.Count - 1);
        }
    }

    public List<DebugLogEntry> GetRecent(int count) => GetFiltered(new DebugLogFilter(), count);

    public List<DebugLogEntry> GetFiltered(DebugLogFilter filter, int count)
    {
        lock (_entries)
        {
            var result = new List<DebugLogEntry>(Math.Min(count, _entries.Count));
            foreach (var entry in _entries)
            {
                if (result.Count >= count) break;
                if (filter.Matches(entry)) result.Add(entry);
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (_entries) _entries.Clear();
    }
}
