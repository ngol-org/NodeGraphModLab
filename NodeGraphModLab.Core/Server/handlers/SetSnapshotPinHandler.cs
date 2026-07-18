using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class SetSnapshotPinHandler : IMessageHandler
{
    public string MessageType => "set_snapshot_pin";

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var nodeId = root.TryGetProperty("nodeInstanceId", out var nid) ? nid.GetString() : null;
        var pinned = root.TryGetProperty("pinned", out var pin) && pin.GetBoolean();
        if (nodeId == null) return;

        if (pinned) session.PinnedSnapshotNodeIds.Add(nodeId);
        else session.PinnedSnapshotNodeIds.Remove(nodeId);

        var push = new SnapshotPinChangedPush { NodeInstanceId = nodeId, Pinned = pinned };
        await session.SendAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.SnapshotPinChangedPush));
    }
}
