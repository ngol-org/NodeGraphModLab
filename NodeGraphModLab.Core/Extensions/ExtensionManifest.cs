using System.Text.Json;
using System.Text.Json.Serialization;

namespace NodeGraphModLab.Core.Extensions;

internal sealed class ExtensionManifest
{
    public const int SupportedApiVersion = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("apiVersion")]
    public int ApiVersion { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = "";

    [JsonPropertyName("entryType")]
    public string EntryType { get; set; } = "";

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("libraries")]
    public ExtensionLibrariesConfig? Libraries { get; set; }

    [JsonPropertyName("nodes")]
    public ExtensionNodesConfig? Nodes { get; set; }

    public static bool TryLoad(string manifestPath, out ExtensionManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;

        if (!File.Exists(manifestPath))
        {
            error = "manifest not found";
            return false;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<ExtensionManifest>(json, JsonOptions);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                error = "invalid manifest: missing id";
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || string.IsNullOrWhiteSpace(manifest.EntryType))
            {
                error = $"invalid manifest for '{manifest.Id}': entryAssembly/entryType required";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

internal sealed class ExtensionLibrariesConfig
{
    [JsonPropertyName("preload")]
    public bool Preload { get; set; } = true;
}

internal sealed class ExtensionNodesConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "dll";

    [JsonPropertyName("directory")]
    public string Directory { get; set; } = "nodes";
}
