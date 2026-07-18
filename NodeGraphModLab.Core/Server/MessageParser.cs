using System.Text.Json;

namespace NodeGraphModLab.Server;

/// <summary>
/// WebSocket メッセージを解析する静的ヘルパー。
/// 全メッセージは {"type": "..."} を持つ JSON オブジェクト。
/// </summary>
internal static class MessageParser
{
    /// <summary>JSON を解析して type フィールドと JsonDocument を返す。パース失敗時は (null, null)。</summary>
    public static (string? type, JsonDocument? document) ParseType(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeElement))
                return (typeElement.GetString(), doc);
            return (null, null);
        }
        catch
        {
            return (null, null);
        }
    }
}
