using System.Text.Json;

namespace NodeGraphModLab.Server;

internal static class MessageHelper
{
    public static HashSet<string> ParseStringList(JsonElement root, string propertyName)
    {
        var result = new HashSet<string>();
        if (root.TryGetProperty(propertyName, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray())
                if (item.GetString() is { } s) result.Add(s);
        return result;
    }
}
