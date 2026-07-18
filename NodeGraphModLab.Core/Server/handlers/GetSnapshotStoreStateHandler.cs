using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// SnapshotStore の全状態を返すハンドラ
/// WebUI 接続時・リフレッシュ時に呼ばれ、savedSnapshots を同期する。
/// </summary>
internal sealed class GetSnapshotStoreStateHandler : IMessageHandler
{
    public string MessageType => "get_snapshot_store_state";

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var allEntries = session.SnapshotStore.GetAllCurrentEntries();
        var dtoList = allEntries.Select(e => new SnapshotStoreEntry
        {
            NodeInstanceId = e.NodeInstanceId,
            PortName = e.PortName,
            ValueType = e.Value?.GetType().Name ?? "null",
            ValueString = e.Value?.ToString(),
        }).ToList();

        var response = new SnapshotStoreStateResponse { Entries = dtoList };
        await session.SendAsync(JsonSerializer.Serialize(response, ServerJsonContext.Default.SnapshotStoreStateResponse));
    }
}
