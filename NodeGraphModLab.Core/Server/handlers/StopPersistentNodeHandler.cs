using System.Linq;
using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class StopPersistentNodeHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "stop_persistent_node";

    public StopPersistentNodeHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        if (!root.TryGetProperty("nodeInstanceId", out var prop))
        {
            await session.SendAsync(JsonSerializer.Serialize(
                new StopPersistentNodeResponse { Found = false },
                ServerJsonContext.Default.StopPersistentNodeResponse));
            return;
        }
        var nodeInstanceId = prop.GetString() ?? "";
        bool found = !string.IsNullOrEmpty(nodeInstanceId) &&
                     _ctx.Runner.GetActiveNodes().Any(n => n.NodeInstanceId == nodeInstanceId);
        if (!string.IsNullOrEmpty(nodeInstanceId))
        {
            _ctx.Runner.CancelByNodeId(nodeInstanceId);
            _ctx.LiveParamStore.ClearNode(nodeInstanceId);
        }
        _ctx.Log?.LogInfo($"[GraphServer] Persistent node stopped: {nodeInstanceId} (found={found})");
        await session.SendAsync(JsonSerializer.Serialize(
            new StopPersistentNodeResponse { NodeInstanceId = nodeInstanceId, Found = found },
            ServerJsonContext.Default.StopPersistentNodeResponse));
    }
}
