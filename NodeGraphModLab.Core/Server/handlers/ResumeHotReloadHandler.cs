using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ResumeHotReloadHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "resume_hot_reload";

    public ResumeHotReloadHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        // 再開直後にまとめてコンパイルされる件数を、フラグを倒す前に確定させておく。
        var pendingCount = _ctx.HotReloadGate.GetPendingCount();
        _ctx.HotReloadGate.Resume();
        _ctx.Log?.LogInfo($"[Scripts] Hot-reload resumed ({pendingCount} file(s) queued for recompilation).");
        await session.SendAsync(JsonSerializer.Serialize(
            new ResumeHotReloadResponse { Paused = false, PendingCount = pendingCount },
            ServerJsonContext.Default.ResumeHotReloadResponse));
    }
}
