using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ListGraphsHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "list_graphs";

    public ListGraphsHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var summaries = GraphPersistenceHelper.ListSummaries(_ctx.GraphSaveDir);
        await session.SendAsync(JsonSerializer.Serialize(
            new ListGraphsResponse { Graphs = summaries },
            ServerJsonContext.Default.ListGraphsResponse));
    }
}
