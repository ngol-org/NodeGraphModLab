using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ExecuteFragmentHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "execute_fragment";

    public ExecuteFragmentHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var graph = NodeGraph.FromJson(root.GetProperty("graph").GetRawText());
        var fragId = root.TryGetProperty("fragmentId", out var fid) ? fid.GetString() : null;
        var pinned = MessageHelper.ParseStringList(root, "pinnedFragmentIds");
        if (graph != null && fragId != null)
            _ctx.PendingExecutions.Enqueue(new PendingExecution(session, graph, fragmentId: fragId, pinnedIds: pinned));
        else
            await session.SendAsync(JsonSerializer.Serialize(
                new ErrorResponse { Message = "Invalid execute_fragment payload" },
                ServerJsonContext.Default.ErrorResponse));
    }
}
