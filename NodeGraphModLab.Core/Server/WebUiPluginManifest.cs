using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NodeGraphModLab.Server;

/// <summary>GET /api/webui-plugins が返すマニフェストの 1 エントリ。</summary>
internal sealed class WebUiPluginManifestEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("scriptUrl")] public string ScriptUrl { get; set; } = string.Empty;
    [JsonPropertyName("apiVersion")] public int? ApiVersion { get; set; }
}

/// <summary>
/// WebUI 外部プラグインのマニフェスト生成。
/// WebUI 配信ディレクトリ直下の plugins/ をスキャンし、2 形式をサポートする:
///   形式A: plugins/*.js           — 単一ファイル（id はファイル名から）
///   形式B: plugins/dir/plugin.json — メタデータ付きフォルダ
/// scriptUrl には対象 JS の最終更新時刻 Ticks を ?v= として付与し、
/// ブラウザの HTTP キャッシュ・ES module キャッシュを更新時に確実にバイパスさせる。
/// </summary>
internal static class WebUiPluginManifest
{
    public const string EndpointPath = "/api/webui-plugins";

    public static string BuildJson(string webUiDir)
    {
        var entries = BuildEntries(webUiDir);
        return JsonSerializer.Serialize(entries, ServerJsonContext.Default.ListWebUiPluginManifestEntry);
    }

    public static List<WebUiPluginManifestEntry> BuildEntries(string webUiDir)
    {
        var result = new List<WebUiPluginManifestEntry>();
        var pluginsDir = Path.Combine(webUiDir, "plugins");
        if (!Directory.Exists(pluginsDir)) return result;

        // 形式A: plugins/ 直下の *.js
        foreach (var jsPath in SafeEnumerate(() => Directory.GetFiles(pluginsDir, "*.js")))
        {
            try
            {
                var fileName = Path.GetFileName(jsPath);
                result.Add(new WebUiPluginManifestEntry
                {
                    Id = Path.GetFileNameWithoutExtension(jsPath),
                    ScriptUrl = $"/plugins/{Uri.EscapeDataString(fileName)}?v={File.GetLastWriteTimeUtc(jsPath).Ticks}"
                });
            }
            catch { /* 壊れたエントリは個別スキップ（残りは返す） */ }
        }

        // 形式B: plugins/<dir>/plugin.json
        foreach (var dir in SafeEnumerate(() => Directory.GetDirectories(pluginsDir)))
        {
            try
            {
                var metaPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(metaPath)) continue;

                using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
                var root = doc.RootElement;

                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;

                var scriptFile = root.TryGetProperty("scriptFile", out var sfEl)
                    ? (sfEl.GetString() ?? "index.js") : "index.js";
                // plugin.json によるフォルダ外参照を拒否
                if (scriptFile.Contains("..")) continue;

                var scriptPath = Path.Combine(dir, scriptFile);
                if (!File.Exists(scriptPath)) continue;

                var dirName = Path.GetFileName(dir);
                result.Add(new WebUiPluginManifestEntry
                {
                    Id = id!,
                    DisplayName = root.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() : null,
                    Version = root.TryGetProperty("version", out var vEl) ? vEl.GetString() : null,
                    ApiVersion = root.TryGetProperty("apiVersion", out var avEl) && avEl.ValueKind == JsonValueKind.Number
                        ? avEl.GetInt32() : null,
                    ScriptUrl = $"/plugins/{Uri.EscapeDataString(dirName)}/{Uri.EscapeDataString(scriptFile)}?v={File.GetLastWriteTimeUtc(scriptPath).Ticks}"
                });
            }
            catch { /* 壊れた plugin.json は個別スキップ */ }
        }

        return result;
    }

    private static string[] SafeEnumerate(Func<string[]> enumerate)
    {
        try { return enumerate(); }
        catch { return Array.Empty<string>(); }
    }
}
