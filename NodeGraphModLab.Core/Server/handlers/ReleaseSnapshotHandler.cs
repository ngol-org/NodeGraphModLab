using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ReleaseSnapshotHandler : IMessageHandler
{
    public string MessageType => "release_snapshot";

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var key = root.TryGetProperty("key", out var keyElem) ? keyElem.GetString() ?? "" : "";
        var parts = key.Split(new[] { ':' }, 2);

        bool released = false;
        if (parts.Length == 2 && !string.IsNullOrEmpty(parts[0]) && !string.IsNullOrEmpty(parts[1]))
        {
            session.SnapshotStore.ClearSnapshot(parts[0], parts[1]);
            released = true;
        }

        await session.SendAsync(JsonSerializer.Serialize(
            new ReleaseSnapshotResponse { Released = released },
            ServerJsonContext.Default.ReleaseSnapshotResponse));
    }
}
