using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class PauseHotReloadHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "pause_hot_reload";

    public PauseHotReloadHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        _ctx.HotReloadGate.Pause();
        var pendingCount = _ctx.HotReloadGate.GetPendingCount();
        _ctx.Log?.LogInfo($"[Scripts] Hot-reload paused (pendingCount={pendingCount}).");
        await session.SendAsync(JsonSerializer.Serialize(
            new PauseHotReloadResponse { Paused = true, PendingCount = pendingCount },
            ServerJsonContext.Default.PauseHotReloadResponse));
    }
}
