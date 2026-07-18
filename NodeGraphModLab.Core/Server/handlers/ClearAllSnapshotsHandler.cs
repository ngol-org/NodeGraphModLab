using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// SnapshotStore の全エントリをクリアするハンドラ
/// </summary>
internal sealed class ClearAllSnapshotsHandler : IMessageHandler
{
    public string MessageType => "clear_all_snapshots";

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        session.SnapshotStore.Clear();
        var push = new AllSnapshotsClearedPush();
        await session.SendAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.AllSnapshotsClearedPush));
    }
}
