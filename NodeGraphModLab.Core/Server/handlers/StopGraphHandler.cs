using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class StopGraphHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "stop_graph";

    public StopGraphHandler(HandlerContext ctx) { _ctx = ctx; }

    public Task HandleAsync(ISession session, JsonElement root)
    {
        _ctx.GetExecutionCts()?.Cancel();
        _ctx.Log?.LogInfo("[GraphServer] Execution stop requested.");
        return Task.CompletedTask;
    }
}
