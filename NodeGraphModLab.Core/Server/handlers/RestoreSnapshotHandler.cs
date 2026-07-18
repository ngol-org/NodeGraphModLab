using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class RestoreSnapshotHandler : IMessageHandler
{
    public string MessageType => "restore_snapshot";

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var nodeId = root.TryGetProperty("nodeInstanceId", out var nid) ? nid.GetString() : null;
        var portName = root.TryGetProperty("portName", out var pn) ? pn.GetString() : null;
        var idx = root.TryGetProperty("historyIndex", out var hi) ? hi.GetInt32() : -1;
        if (nodeId == null || portName == null || idx < 0) return;

        var store = session.SnapshotStore;
        if (!store.RestoreSnapshot(nodeId, portName, idx)) return;

        var restoredValue = store.GetSnapshot(nodeId, portName);
        var push = new SnapshotRestoredPush
        {
            NodeInstanceId = nodeId,
            PortName = portName,
            ValueType = restoredValue?.GetType().Name ?? "null",
            ValueString = restoredValue?.ToString()
        };
        await session.SendAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.SnapshotRestoredPush));
    }
}
