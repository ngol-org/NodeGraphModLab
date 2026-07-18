using System.Text.Json.Serialization;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

// ============================================================
// 受信メッセージ型（クライアント → サーバー）
// DTO はすべて ServerDtos.cs, JSON コンテキストは ServerJsonContext.cs を参照
// ============================================================

public abstract class InboundMessage
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

public sealed class GetNodeListMessage : InboundMessage
{
    public override string Type => "get_node_list";
}

public sealed class ExecuteGraphMessage : InboundMessage
{
    public override string Type => "execute_graph";

    [JsonPropertyName("graph")]
    public NodeGraph? Graph { get; set; }
}

public sealed class SaveGraphMessage : InboundMessage
{
    public override string Type => "save_graph";

    [JsonPropertyName("graph")]
    public NodeGraph? Graph { get; set; }
}

public sealed class LoadGraphMessage : InboundMessage
{
    public override string Type => "load_graph";

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public sealed class ListGraphsMessage : InboundMessage
{
    public override string Type => "list_graphs";
}

public sealed class DeleteGraphMessage : InboundMessage
{
    public override string Type => "delete_graph";

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public sealed class StopPersistentMessage : InboundMessage
{
    public override string Type => "stop_persistent";
}

public sealed class CompileNodeMessage : InboundMessage
{
    public override string Type => "compile_node";

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("className")]
    public string? ClassName { get; set; }
}

public sealed class GetGameStateMessage : InboundMessage
{
    public override string Type => "get_game_state";
}

public sealed class ExportNodesMessage : InboundMessage
{
    public override string Type => "export_nodes";

    [JsonPropertyName("assemblyName")]
    public string AssemblyName { get; set; } = "";

    [JsonPropertyName("outputDir")]
    public string OutputDir { get; set; } = "";

    [JsonPropertyName("nodeTypeIds")]
    public List<string> NodeTypeIds { get; set; } = new();
}
