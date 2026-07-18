using System.Text.Json;

namespace NodeGraphModLab.Core.Extensions;

/// <summary>
/// ロード済み Extension の一覧 API（<see cref="EndpointPath"/>）。
/// </summary>
public static class ExtensionManifestApi
{
    public const string EndpointPath = "/api/extensions";

    public static IReadOnlyList<ExtensionManifestEntry> BuildEntries(ExtensionHost? host)
        => host?.GetManifestEntries() ?? Array.Empty<ExtensionManifestEntry>();

    public static string BuildJson(ExtensionHost? host)
        => JsonSerializer.Serialize(BuildEntries(host), JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
