using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class StopPersistentHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "stop_persistent";

    public StopPersistentHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        int count = _ctx.Runner.GetActiveNodes().Count;
        _ctx.Runner.ClearAll();
        _ctx.LiveParamStore.ClearAll();
        _ctx.Log?.LogInfo($"[GraphServer] Persistent node callbacks cleared ({count} stopped).");
        await session.SendAsync(JsonSerializer.Serialize(
            new StopPersistentResponse { StoppedCount = count },
            ServerJsonContext.Default.StopPersistentResponse));
    }
}
