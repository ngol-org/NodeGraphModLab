using System.Text.Json.Serialization;

namespace NodeGraphModLab.Core.Extensions;

/// <summary>GET /api/extensions が返す 1 エントリ。</summary>
public sealed class ExtensionManifestEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("capabilities")] public List<ExtensionCapabilityEntry> Capabilities { get; set; } = new();
}

public sealed class ExtensionCapabilityEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string? Version { get; set; }
}
