using System.Text.Json;
using System.Text.Json.Serialization;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

// =========================================================
// 送受信 DTO
// =========================================================

// --- クライアント → サーバー ---

public sealed class GetNodeListRequest
{
    [JsonPropertyName("type")] public string Type => "get_node_list";
}

public sealed class ExecuteGraphRequest
{
    [JsonPropertyName("type")] public string Type => "execute_graph";
    [JsonPropertyName("graph")] public object? Graph { get; set; }
}

public sealed class SaveGraphRequest
{
    [JsonPropertyName("type")] public string Type => "save_graph";
    [JsonPropertyName("graph")] public object? Graph { get; set; }
}

public sealed class LoadGraphRequest
{
    [JsonPropertyName("type")] public string Type => "load_graph";
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public sealed class ListGraphsRequest
{
    [JsonPropertyName("type")] public string Type => "list_graphs";
}

public sealed class DeleteGraphRequest
{
    [JsonPropertyName("type")] public string Type => "delete_graph";
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public sealed class CompileNodeRequest
{
    [JsonPropertyName("type")] public string Type => "compile_node";
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("className")] public string? ClassName { get; set; }
    [JsonPropertyName("persist")] public bool Persist { get; set; }
}

public sealed class ExecuteFragmentRequest
{
    [JsonPropertyName("type")]              public string Type => "execute_fragment";
    [JsonPropertyName("graph")]            public object? Graph { get; set; }
    [JsonPropertyName("fragmentId")]       public string FragmentId { get; set; } = "";
    [JsonPropertyName("pinnedFragmentIds")] public List<string> PinnedFragmentIds { get; set; } = new();
}

public sealed class ExecuteAllFragmentsRequest
{
    [JsonPropertyName("type")]              public string Type => "execute_all_fragments";
    [JsonPropertyName("graph")]            public object? Graph { get; set; }
    [JsonPropertyName("pinnedFragmentIds")] public List<string> PinnedFragmentIds { get; set; } = new();
}

public sealed class SetSnapshotPinRequest
{
    [JsonPropertyName("type")]           public string Type => "set_snapshot_pin";
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("pinned")]         public bool Pinned { get; set; }
}


// --- サーバー → クライアント ---

public sealed class NodeTypeInfo
{
    [JsonPropertyName("type")] public string Type => "node_type_info";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("nodeVersion")] public string NodeVersion { get; set; } = "1.0.0";
    [JsonPropertyName("ports")] public List<PortInfo> Ports { get; set; } = new();
    [JsonPropertyName("filePath")] public string? FilePath { get; set; }
    [JsonPropertyName("customWebUi")] public string? CustomWebUi { get; set; }
    /// <summary>カスタムノード(.cs)の最終更新日時（ISO8601）。DLL経由のノードはnull。</summary>
    [JsonPropertyName("lastModified")] public string? LastModified { get; set; }
}

public sealed class PortInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("direction")] public string Direction { get; set; } = "";
    [JsonPropertyName("dataType")] public string DataType { get; set; } = "";
    [JsonPropertyName("isRequired")] public bool IsRequired { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("showInlineEditor")] public bool ShowInlineEditor { get; set; }
}

public sealed class NodeListResponse
{
    [JsonPropertyName("type")] public string Type => "node_list_response";
    [JsonPropertyName("nodes")] public List<NodeTypeInfo> Nodes { get; set; } = new();
}

public sealed class ExecutionResultResponse
{
    [JsonPropertyName("type")]        public string Type => "execution_result";
    [JsonPropertyName("success")]     public bool Success { get; set; }
    [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("durationMs")]  public double DurationMs { get; set; }
    /// <summary>断片実行時のみ設定。どの断片の結果かUIで区別するため。</summary>
    [JsonPropertyName("fragmentId")]  public string? FragmentId { get; set; }
    /// <summary>このグラフ実行中に登録された永続ノードJobの一覧（jobIdとどのノードのものかの対応）。</summary>
    [JsonPropertyName("jobs")]        public List<JobRef> Jobs { get; set; } = new();
}

/// <summary>Jobとそれがどのノードに属するかの対応。execute_graph系/run_nodeのレスポンスに付与する。</summary>
public sealed class JobRef
{
    [JsonPropertyName("jobId")]         public string JobId { get; set; } = "";
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
}

/// <summary>async:true 指定時、実行を待たずに即座に返すレスポンス。</summary>
public sealed class JobStartedResponse
{
    [JsonPropertyName("type")]  public string Type => "job_started";
    [JsonPropertyName("jobId")] public string JobId { get; set; } = "";
}

/// <summary>check_job_status のレスポンス。</summary>
public sealed class JobStatusResponse
{
    [JsonPropertyName("type")]           public string Type => "job_status_response";
    [JsonPropertyName("found")]          public bool Found { get; set; }
    [JsonPropertyName("jobId")]          public string JobId { get; set; } = "";
    [JsonPropertyName("kind")]           public string? Kind { get; set; }
    [JsonPropertyName("nodeInstanceId")] public string? NodeInstanceId { get; set; }
    [JsonPropertyName("state")]          public string? State { get; set; }
    /// <summary>進捗メモ・完了メモ・失敗理由をまとめた自由記述フィールド（オプトイン、既定はnull）。</summary>
    [JsonPropertyName("message")]        public string? Message { get; set; }
}

public sealed class ExecutionLogPush
{
    [JsonPropertyName("type")] public string Type => "execution_log";
    [JsonPropertyName("nodeInstanceId")] public string? NodeInstanceId { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("level")] public string Level { get; set; } = "info";
    [JsonPropertyName("timestampMs")] public long TimestampMs { get; set; }
}

public sealed class SaveGraphResponse
{
    [JsonPropertyName("type")] public string Type => "save_graph_response";
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public sealed class LoadGraphResponse
{
    [JsonPropertyName("type")] public string Type => "load_graph_response";
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("graph")] public NodeGraph? Graph { get; set; }
}

public sealed class OpenGraphPush
{
    [JsonPropertyName("type")] public string Type => "open_graph_push";
    [JsonPropertyName("graphId")] public string GraphId { get; set; } = "";
}

public sealed class OpenGraphResponse
{
    [JsonPropertyName("type")] public string Type => "open_graph_response";
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("delivered")] public bool Delivered { get; set; }
    [JsonPropertyName("graphId")] public string? GraphId { get; set; }
}

public sealed class ListGraphsResponse
{
    [JsonPropertyName("type")] public string Type => "list_graphs_response";
    [JsonPropertyName("graphs")] public List<GraphSummary> Graphs { get; set; } = new();
}

public sealed class GraphSummary
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
}

public sealed class DeleteGraphResponse
{
    [JsonPropertyName("type")] public string Type => "delete_graph_response";
    [JsonPropertyName("success")] public bool Success { get; set; }
}

public sealed class CompileNodeResponse
{
    [JsonPropertyName("type")] public string Type => "compile_node_response";
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("nodeId")] public string? NodeId { get; set; }
    /// <summary>コンパイルされたアセンブリに含まれる全ノードTypeId（1ファイル複数クラス対応）。</summary>
    [JsonIgnore] public List<string> NodeIds { get; set; } = new();
    [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("diagnostics")] public List<string>? Diagnostics { get; set; }
    [JsonPropertyName("persisted")] public bool Persisted { get; set; }
    [JsonPropertyName("savedDllPath")] public string? SavedDllPath { get; set; }
}

public sealed class ErrorResponse
{
    [JsonPropertyName("type")] public string Type => "error";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public sealed class ExecutionProgressPush
{
    [JsonPropertyName("type")] public string Type => "execution_progress";
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    /// <summary>"running" | "done" | "error"</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("durationMs")] public double DurationMs { get; set; }
}

public sealed class SnapshotSavedPush
{
    [JsonPropertyName("type")]           public string Type => "snapshot_saved";
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("portName")]       public string PortName { get; set; } = "";
    [JsonPropertyName("valueType")]      public string ValueType { get; set; } = "";
    [JsonPropertyName("valueString")]    public string? ValueString { get; set; }
}

public sealed class SnapshotPinChangedPush
{
    [JsonPropertyName("type")]           public string Type => "snapshot_pin_changed";
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("pinned")]         public bool Pinned { get; set; }
}

public sealed class PersistentNodeInfo
{
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("displayName")]    public string DisplayName { get; set; } = "";
    [JsonPropertyName("graphName")]      public string GraphName { get; set; } = "";
}

public sealed class PersistentNodeChangedPush
{
    [JsonPropertyName("type")]        public string Type => "persistent_node_changed";
    [JsonPropertyName("activeNodes")] public List<PersistentNodeInfo> ActiveNodes { get; set; } = new();
}

public sealed class SnapshotHistoryEntryDto
{
    [JsonPropertyName("valueType")]   public string ValueType { get; set; } = "";
    [JsonPropertyName("valueString")] public string? ValueString { get; set; }
    [JsonPropertyName("timestampMs")] public long TimestampMs { get; set; }
}

public sealed class SnapshotHistoryResponse
{
    [JsonPropertyName("type")]           public string Type => "snapshot_history";
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("portName")]       public string PortName { get; set; } = "";
    [JsonPropertyName("entries")]        public List<SnapshotHistoryEntryDto> Entries { get; set; } = new();
}

public sealed class SnapshotRestoredPush
{
    [JsonPropertyName("type")]           public string Type => "snapshot_restored";
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("portName")]       public string PortName { get; set; } = "";
    [JsonPropertyName("valueType")]      public string ValueType { get; set; } = "";
    [JsonPropertyName("valueString")]    public string? ValueString { get; set; }
}

public sealed class SetSnapshotValueResponse
{
    [JsonPropertyName("type")]           public string Type => "set_snapshot_value_response";
    [JsonPropertyName("success")]        public bool Success { get; set; }
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("portName")]       public string PortName { get; set; } = "";
    /// <summary>失敗理由: "blocked"（PIN 中）/ "unsupported_type"（プリミティブ以外）。成功時は null。</summary>
    [JsonPropertyName("reason")]         public string? Reason { get; set; }
}

public sealed class PushNodeLiveParamsResponse
{
    [JsonPropertyName("type")]           public string Type => "push_node_live_params_response";
    [JsonPropertyName("success")]        public bool Success { get; set; }
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("mergedKeys")]     public List<string> MergedKeys { get; set; } = new();
    /// <summary>失敗理由: "missing_params" / "unsupported_type" / "no_valid_params"。成功時は null。</summary>
    [JsonPropertyName("reason")]         public string? Reason { get; set; }
}

public sealed class SnapshotClearedPush
{
    [JsonPropertyName("type")]           public string Type => "snapshot_cleared";
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("portName")]       public string PortName { get; set; } = "";
}

/// <summary>
/// SnapshotStore の全エントリをクリアしたことを WebUI へ通知するプッシュメッセージ
/// </summary>
public sealed class AllSnapshotsClearedPush
{
    [JsonPropertyName("type")] public string Type => "all_snapshots_cleared";
}

/// <summary>
/// SnapshotStore の個別エントリ
/// </summary>
public sealed class SnapshotStoreEntry
{
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("portName")]       public string PortName { get; set; } = "";
    [JsonPropertyName("valueType")]      public string ValueType { get; set; } = "";
    [JsonPropertyName("valueString")]    public string? ValueString { get; set; }
}

/// <summary>
/// SnapshotStore の全状態レスポンス
/// </summary>
public sealed class SnapshotStoreStateResponse
{
    [JsonPropertyName("type")]    public string Type => "snapshot_store_state";
    [JsonPropertyName("entries")] public List<SnapshotStoreEntry> Entries { get; set; } = new();
}

/// <summary>
/// ノードタイプ一覧が更新されたことを WebUI へ通知するプッシュメッセージ。
/// </summary>
public sealed class NodeListUpdatedPush
{
    [JsonPropertyName("type")]               public string Type => "node_list_updated";
    [JsonPropertyName("updatedNodeTypeIds")] public List<string> UpdatedNodeTypeIds { get; set; } = new();
}

/// <summary>
/// スクリプト(.cs)のコンパイル失敗を WebUI へ通知するプッシュメッセージ。
/// 起動時一括コンパイル・ホットリロード両方で使用する。
/// </summary>
public sealed class ScriptCompileErrorPush
{
    [JsonPropertyName("type")]         public string Type => "script_compile_error";
    [JsonPropertyName("fileName")]     public string FileName { get; set; } = "";
    [JsonPropertyName("errorMessage")] public string ErrorMessage { get; set; } = "";
    [JsonPropertyName("diagnostics")]  public List<string>? Diagnostics { get; set; }
}

public sealed class ExportNodesRequest
{
    [JsonPropertyName("type")]         public string Type => "export_nodes";
    [JsonPropertyName("assemblyName")] public string AssemblyName { get; set; } = "";
    [JsonPropertyName("outputDir")]    public string OutputDir { get; set; } = "";
    [JsonPropertyName("nodeTypeIds")]  public List<string> NodeTypeIds { get; set; } = new();
}

public sealed class ExportNodesResponse
{
    [JsonPropertyName("type")]           public string Type => "export_nodes_response";
    [JsonPropertyName("success")]        public bool Success { get; set; }
    [JsonPropertyName("savedPath")]      public string? SavedPath { get; set; }
    [JsonPropertyName("skippedTypeIds")] public List<string>? SkippedTypeIds { get; set; }
    [JsonPropertyName("errorMessage")]   public string? ErrorMessage { get; set; }
}

public sealed class StopPersistentResponse
{
    [JsonPropertyName("type")]         public string Type => "stop_persistent_response";
    [JsonPropertyName("stoppedCount")] public int StoppedCount { get; set; }
}

public sealed class StopPersistentNodeResponse
{
    [JsonPropertyName("type")]           public string Type => "stop_persistent_node_response";
    [JsonPropertyName("nodeInstanceId")] public string NodeInstanceId { get; set; } = "";
    [JsonPropertyName("found")]          public bool Found { get; set; }
}

public sealed class ListPersistentNodesResponse
{
    [JsonPropertyName("type")]  public string Type => "list_persistent_nodes_response";
    [JsonPropertyName("nodes")] public List<PersistentNodeInfo> Nodes { get; set; } = new();
}

public sealed class ExecuteNodeResponse
{
    [JsonPropertyName("type")]         public string Type => "execute_node_response";
    [JsonPropertyName("success")]      public bool Success { get; init; }
    [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; init; }
    [JsonPropertyName("durationMs")]   public double DurationMs { get; init; }
    [JsonPropertyName("outputs")]      public Dictionary<string, JsonElement> Outputs { get; init; } = new();
    [JsonPropertyName("logs")]         public List<string> Logs { get; init; } = new();
    /// <summary>このノード実行中に登録された永続ノードJobの一覧。</summary>
    [JsonPropertyName("jobs")]         public List<JobRef> Jobs { get; init; } = new();
}

public sealed class ReleaseSnapshotResponse
{
    [JsonPropertyName("type")]     public string Type => "release_snapshot_response";
    [JsonPropertyName("released")] public bool Released { get; init; }
}

public sealed class WelcomeMessage
{
    [JsonPropertyName("type")]           public string Type => "welcome";
    [JsonPropertyName("pluginVersion")]  public string PluginVersion { get; set; } = "";
    [JsonPropertyName("pluginDir")]      public string PluginDir { get; set; } = "";
    [JsonPropertyName("runtimeType")]    public string RuntimeType { get; set; } = "";
    [JsonPropertyName("gameName")]       public string GameName { get; set; } = "";
}

public sealed class DebugLogEntryDto
{
    [JsonPropertyName("kind")]        public string Kind { get; set; } = "";
    [JsonPropertyName("level")]       public string Level { get; set; } = "";
    [JsonPropertyName("message")]     public string Message { get; set; } = "";
    [JsonPropertyName("timestampMs")] public long TimestampMs { get; set; }
}

public sealed class GetDebugLogResponse
{
    [JsonPropertyName("type")]    public string Type => "get_debug_log_response";
    [JsonPropertyName("entries")] public List<DebugLogEntryDto> Entries { get; set; } = new();
}
