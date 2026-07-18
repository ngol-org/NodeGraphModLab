using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ClearSnapshotHandler : IMessageHandler
{
    public string MessageType => "clear_snapshot";

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var nodeId = root.TryGetProperty("nodeInstanceId", out var nid) ? nid.GetString() : null;
        var portName = root.TryGetProperty("portName", out var pn) ? pn.GetString() : null;
        if (nodeId == null) return;

        var store = session.SnapshotStore;
        if (!string.IsNullOrEmpty(portName))
            store.ClearSnapshot(nodeId, portName!);
        else
            store.ClearAllSnapshotsForNode(nodeId);

        var push = new SnapshotClearedPush { NodeInstanceId = nodeId, PortName = portName ?? "" };
        await session.SendAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.SnapshotClearedPush));
    }
}
