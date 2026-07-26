using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NodeGraphModLab;

internal class NgolConfigData
{
    public int Port { get; set; } = NgolConfig.DefaultPort;
    public bool ForceDirectMode { get; set; } = false;
    public int DirectModeIntervalMs { get; set; } = NgolConfig.DefaultDirectModeIntervalMs;
    public string StartupGraphId { get; set; } = "";
    public string StartupNodeTypeId { get; set; } = "";
    public string? StartupNodeInputsJson { get; set; }
    public List<string> CustomNodeDirectories { get; set; } = new();
    public List<string> PriorityCompileNodeTypeIds { get; set; } = new();
    public bool RequireAuthToken { get; set; } = false;
    public bool LogTimestamps { get; set; } = false;
    public string KvStoreBackend { get; set; } = "auto";
    public string KvStoreMigrateFrom { get; set; } = "";
}

public static class NgolConfig
{
    public const int DefaultPort = 11156;
    public const int DefaultDirectModeIntervalMs = 50;
    private static NgolConfigData _data = new NgolConfigData();

    public static int Port => _data.Port;
    public static bool ForceDirectMode => _data.ForceDirectMode;
    public static int DirectModeIntervalMs => _data.DirectModeIntervalMs;
    public static string StartupGraphId => _data.StartupGraphId;
    public static string StartupNodeTypeId => _data.StartupNodeTypeId;
    public static string? StartupNodeInputsJson => _data.StartupNodeInputsJson;
    public static IReadOnlyList<string> CustomNodeDirectories => _data.CustomNodeDirectories;

    /// <summary>
    /// 起動時に最優先でコンパイルするノードタイプ ID（記載順にコンパイルされる）。
    /// 起動時自動実行の対象グラフ・ノードは自動的に優先されるため、ここへの記載は不要。
    /// 登録順そのものに意味があるノードを明示指定するための設定。
    /// </summary>
    public static IReadOnlyList<string> PriorityCompileNodeTypeIds => _data.PriorityCompileNodeTypeIds;

    public static bool RequireAuthToken => _data.RequireAuthToken;

    /// <summary>
    /// ログ1行の先頭に時刻（[HH:mm:ss.fff]）を付与するか。既定は false。
    /// ホスト側のログ機構が時刻を出力しない環境で、起動時のタイミングを追いたい場合に有効化する。
    /// 時刻を常に自前で付与するロガー実装ではこの設定は参照されない。
    /// </summary>
    public static bool LogTimestamps => _data.LogTimestamps;

    /// <summary>
    /// キー・値ストアの永続化に使う実装の名前。既定は "auto"。
    /// 指定できる値は Core.KVStore.KVStoreBackendFactory.KnownNames を参照。
    /// 既定で足りる場合は設定ファイルに書く必要はない。
    /// </summary>
    public static string KvStoreBackend => _data.KvStoreBackend;

    /// <summary>
    /// 保存先を切り替えたときに、以前の保存先から中身を引き継ぐための一度きりの指定。
    /// 空文字（既定）なら何もしない。引き継ぎ済みかどうかは保存先ディレクトリの記録ファイルで判定する。
    /// </summary>
    public static string KvStoreMigrateFrom => _data.KvStoreMigrateFrom;

    public static void Load(string pluginDir, INgolLogger log)
    {
        _data = new NgolConfigData();
        var path = Path.Combine(pluginDir, "ngol-config.json");

        if (!File.Exists(path))
        {
            WriteDefault(path);
            log.LogInfo($"[Config] Created ngol-config.json with defaults (port={DefaultPort})");
            return;
        }

        try
        {
            var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;

                if (root.TryGetProperty("port", out var portElement))
                {
                    var port = portElement.GetInt32();
                    if (port >= 1 && port <= 65535)
                    {
                        _data.Port = port;
                    }
                    else if (port != 0)
                    {
                        log.LogWarning($"[Config] port={port} is out of range, using default {DefaultPort}");
                    }
                }

                if (root.TryGetProperty("forceDirectMode", out var forceDirectModeElement))
                {
                    _data.ForceDirectMode = forceDirectModeElement.GetBoolean();
                }

                if (root.TryGetProperty("directModeIntervalMs", out var intervalElement))
                {
                    var interval = intervalElement.GetInt32();
                    if (interval >= 1 && interval <= 10000)
                    {
                        _data.DirectModeIntervalMs = interval;
                    }
                    else
                    {
                        log.LogWarning($"[Config] directModeIntervalMs={interval} is out of range, using default {DefaultDirectModeIntervalMs}");
                    }
                }

                if (root.TryGetProperty("startupGraphId", out var startupGraphIdElement))
                {
                    _data.StartupGraphId = startupGraphIdElement.GetString() ?? "";
                }

                if (root.TryGetProperty("startupNodeTypeId", out var startupNodeTypeIdElement))
                {
                    _data.StartupNodeTypeId = startupNodeTypeIdElement.GetString() ?? "";
                }

                if (root.TryGetProperty("startupNodeInputs", out var startupNodeInputsElement))
                {
                    _data.StartupNodeInputsJson = startupNodeInputsElement.GetRawText();
                }

                if (root.TryGetProperty("customNodeDirectories", out var dirsElement) && dirsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dirsElement.EnumerateArray())
                    {
                        var dir = item.GetString();
                        if (string.IsNullOrWhiteSpace(dir))
                        {
                            log.LogWarning("[Config] customNodeDirectories contains an empty entry, skipping");
                            continue;
                        }
                        _data.CustomNodeDirectories.Add(dir);
                    }
                }

                if (root.TryGetProperty("priorityCompileNodeTypeIds", out var priorityElement) && priorityElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in priorityElement.EnumerateArray())
                    {
                        var nodeTypeId = (item.ValueKind == JsonValueKind.String ? item.GetString() : null) ?? "";
                        nodeTypeId = nodeTypeId.Trim();
                        if (nodeTypeId.Length == 0)
                        {
                            log.LogWarning("[Config] priorityCompileNodeTypeIds contains an empty entry, skipping");
                            continue;
                        }
                        _data.PriorityCompileNodeTypeIds.Add(nodeTypeId);
                    }
                }

                if (root.TryGetProperty("requireAuthToken", out var requireAuthTokenElement))
                {
                    _data.RequireAuthToken = requireAuthTokenElement.GetBoolean();
                }

                if (root.TryGetProperty("logTimestamps", out var logTimestampsElement))
                {
                    _data.LogTimestamps = logTimestampsElement.GetBoolean();
                }

                if (root.TryGetProperty("kvStoreBackend", out var kvStoreBackendElement)
                    && kvStoreBackendElement.ValueKind == JsonValueKind.String)
                {
                    var backendName = kvStoreBackendElement.GetString();
                    if (!string.IsNullOrWhiteSpace(backendName))
                    {
                        _data.KvStoreBackend = backendName!.Trim().ToLowerInvariant();
                    }
                }

                if (root.TryGetProperty("kvStoreMigrateFrom", out var kvStoreMigrateFromElement)
                    && kvStoreMigrateFromElement.ValueKind == JsonValueKind.String)
                {
                    var migrateFrom = kvStoreMigrateFromElement.GetString();
                    if (!string.IsNullOrWhiteSpace(migrateFrom))
                    {
                        _data.KvStoreMigrateFrom = migrateFrom!.Trim().ToLowerInvariant();
                    }
                }

                log.LogInfo($"[Config] Loaded: port={_data.Port} forceDirectMode={_data.ForceDirectMode} directModeIntervalMs={_data.DirectModeIntervalMs} customNodeDirectories={_data.CustomNodeDirectories.Count} priorityCompileNodeTypeIds={_data.PriorityCompileNodeTypeIds.Count} requireAuthToken={_data.RequireAuthToken} logTimestamps={_data.LogTimestamps} kvStoreBackend={_data.KvStoreBackend} kvStoreMigrateFrom={(_data.KvStoreMigrateFrom.Length == 0 ? "(none)" : _data.KvStoreMigrateFrom)}");
            }
        }
        catch (Exception ex)
        {
            log.LogWarning($"[Config] Failed to parse ngol-config.json: {ex.Message}, using default {DefaultPort}");
            _data = new NgolConfigData();
        }
    }

    private static void WriteDefault(string path)
    {
        File.WriteAllText(path,
            "{\n  \"port\": " + DefaultPort + ",\n  \"forceDirectMode\": false,\n  \"requireAuthToken\": false,\n  \"logTimestamps\": false\n}\n",
            System.Text.Encoding.UTF8);
    }
}
