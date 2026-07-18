using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ExecuteAllFragmentsHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "execute_all_fragments";

    public ExecuteAllFragmentsHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var graph = NodeGraph.FromJson(root.GetProperty("graph").GetRawText());
        var pinned = MessageHelper.ParseStringList(root, "pinnedFragmentIds");
        if (graph != null)
            _ctx.PendingExecutions.Enqueue(new PendingExecution(session, graph, executeAll: true, pinnedIds: pinned));
        else
            await session.SendAsync(JsonSerializer.Serialize(
                new ErrorResponse { Message = "Invalid execute_all_fragments payload" },
                ServerJsonContext.Default.ErrorResponse));
    }
}
