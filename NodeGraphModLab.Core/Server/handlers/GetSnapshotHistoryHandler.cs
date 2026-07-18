using System.Linq;
using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class GetSnapshotHistoryHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "get_snapshot_history";

    public GetSnapshotHistoryHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var nodeId = root.TryGetProperty("nodeInstanceId", out var nid) ? nid.GetString() : null;
        var portName = root.TryGetProperty("portName", out var pn) ? pn.GetString() : null;
        if (nodeId == null) return;

        var store = session.SnapshotStore;
        _ctx.Log?.LogInfo($"[History] Query nodeId={nodeId} portName={portName} | {store.DebugDump()}");

        string resolvedPort;
        List<NodeGraphModLab.NodeAPI.SnapshotHistoryEntry> entries;
        if (!string.IsNullOrEmpty(portName))
        {
            resolvedPort = portName!;
            entries = store.GetHistory(nodeId, portName!);
        }
        else
        {
            var found = store.GetFirstHistoryForNode(nodeId);
            resolvedPort = found?.PortName ?? "";
            entries = found?.Entries ?? new List<NodeGraphModLab.NodeAPI.SnapshotHistoryEntry>();
        }
        _ctx.Log?.LogInfo($"[History] Resolved port={resolvedPort} entries={entries.Count}");

        var dtoList = entries.Select(e => new SnapshotHistoryEntryDto
        {
            ValueType = e.ValueType,
            ValueString = e.ValueString,
            TimestampMs = new DateTimeOffset(e.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds()
        }).ToList();
        var resp = new SnapshotHistoryResponse { NodeInstanceId = nodeId, PortName = resolvedPort, Entries = dtoList };
        await session.SendAsync(JsonSerializer.Serialize(resp, ServerJsonContext.Default.SnapshotHistoryResponse));
    }
}
