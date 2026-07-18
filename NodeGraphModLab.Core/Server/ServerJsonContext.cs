using System.Text.Json;
using System.Text.Json.Serialization;
using NodeGraphModLab.Core.Extensions;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

/// <summary>
/// System.Text.Json のソースジェネレーター コンテキスト。
/// AOT / IL2CPP 互換のため、シリアライズ対象型を全て登録する。
/// </summary>
[JsonSerializable(typeof(NodeListResponse))]
[JsonSerializable(typeof(NodeTypeInfo))]
[JsonSerializable(typeof(PortInfo))]
[JsonSerializable(typeof(ExecutionResultResponse))]
[JsonSerializable(typeof(ExecutionLogPush))]
[JsonSerializable(typeof(SaveGraphResponse))]
[JsonSerializable(typeof(LoadGraphResponse))]
[JsonSerializable(typeof(OpenGraphPush))]
[JsonSerializable(typeof(OpenGraphResponse))]
[JsonSerializable(typeof(ListGraphsResponse))]
[JsonSerializable(typeof(GraphSummary))]
[JsonSerializable(typeof(DeleteGraphResponse))]
[JsonSerializable(typeof(CompileNodeResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(ExecutionProgressPush))]
[JsonSerializable(typeof(SnapshotSavedPush))]
[JsonSerializable(typeof(SnapshotPinChangedPush))]
[JsonSerializable(typeof(SnapshotHistoryResponse))]
[JsonSerializable(typeof(SnapshotHistoryEntryDto))]
[JsonSerializable(typeof(List<SnapshotHistoryEntryDto>))]
[JsonSerializable(typeof(SnapshotRestoredPush))]
[JsonSerializable(typeof(SnapshotClearedPush))]
[JsonSerializable(typeof(AllSnapshotsClearedPush))]
[JsonSerializable(typeof(SnapshotStoreEntry))]
[JsonSerializable(typeof(List<SnapshotStoreEntry>))]
[JsonSerializable(typeof(SnapshotStoreStateResponse))]
[JsonSerializable(typeof(NodeListUpdatedPush))]
[JsonSerializable(typeof(ScriptCompileErrorPush))]
[JsonSerializable(typeof(ExportNodesResponse))]
[JsonSerializable(typeof(PersistentNodeInfo))]
[JsonSerializable(typeof(List<PersistentNodeInfo>))]
[JsonSerializable(typeof(PersistentNodeChangedPush))]
[JsonSerializable(typeof(StopPersistentResponse))]
[JsonSerializable(typeof(StopPersistentNodeResponse))]
[JsonSerializable(typeof(ListPersistentNodesResponse))]
[JsonSerializable(typeof(ExecuteNodeResponse))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(ReleaseSnapshotResponse))]
[JsonSerializable(typeof(WelcomeMessage))]
[JsonSerializable(typeof(NodeGraph))]
[JsonSerializable(typeof(List<NodeTypeInfo>))]
[JsonSerializable(typeof(List<PortInfo>))]
[JsonSerializable(typeof(List<GraphSummary>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(ExecuteFragmentRequest))]
[JsonSerializable(typeof(ExecuteAllFragmentsRequest))]
[JsonSerializable(typeof(WebUiPluginManifestEntry))]
[JsonSerializable(typeof(List<WebUiPluginManifestEntry>))]
[JsonSerializable(typeof(ExtensionManifestEntry))]
[JsonSerializable(typeof(ExtensionCapabilityEntry))]
[JsonSerializable(typeof(List<ExtensionManifestEntry>))]
[JsonSerializable(typeof(SetSnapshotValueResponse))]
[JsonSerializable(typeof(PushNodeLiveParamsResponse))]
[JsonSerializable(typeof(DebugLogEntryDto))]
[JsonSerializable(typeof(List<DebugLogEntryDto>))]
[JsonSerializable(typeof(GetDebugLogResponse))]
internal sealed partial class ServerJsonContext : JsonSerializerContext
{
}
