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
    public bool RequireAuthToken { get; set; } = false;
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
    public static bool RequireAuthToken => _data.RequireAuthToken;

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

                if (root.TryGetProperty("requireAuthToken", out var requireAuthTokenElement))
                {
                    _data.RequireAuthToken = requireAuthTokenElement.GetBoolean();
                }

                log.LogInfo($"[Config] Loaded: port={_data.Port} forceDirectMode={_data.ForceDirectMode} directModeIntervalMs={_data.DirectModeIntervalMs} customNodeDirectories={_data.CustomNodeDirectories.Count} requireAuthToken={_data.RequireAuthToken}");
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
            "{\n  \"port\": " + DefaultPort + ",\n  \"forceDirectMode\": false,\n  \"requireAuthToken\": false\n}\n",
            System.Text.Encoding.UTF8);
    }
}
