using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ExecuteGraphHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "execute_graph";

    public ExecuteGraphHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var graph = NodeGraph.FromJson(root.GetProperty("graph").GetRawText());
        if (graph != null)
            _ctx.PendingExecutions.Enqueue(new PendingExecution(session, graph));
        else
            await session.SendAsync(JsonSerializer.Serialize(
                new ErrorResponse { Message = "Invalid graph payload" },
                ServerJsonContext.Default.ErrorResponse));
    }
}
