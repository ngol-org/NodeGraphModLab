using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// ブラウザへプッシュするハンドラが共有する送信先指定の読み取り。
/// 値は "all" / "latest" / セッション識別子 のいずれか。
/// </summary>
internal static class BrowserTarget
{
    /// <summary>
    /// メッセージから target を読む。未指定・空文字は defaultTarget を返す。
    /// 未知の文字列はセッション識別子として扱うため、ここでは弾かない
    /// （一致しなければ送信先0件になる）。
    /// </summary>
    public static string Read(JsonElement root, string defaultTarget)
    {
        if (!root.TryGetProperty("target", out var el) || el.ValueKind != JsonValueKind.String)
            return defaultTarget;
        var value = el.GetString();
        return string.IsNullOrWhiteSpace(value) ? defaultTarget : value!;
    }
}
