using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class LoadGraphHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "load_graph";

    public LoadGraphHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var id = root.GetProperty("id").GetString();
        var graph = id != null ? GraphPersistenceHelper.TryLoad(id, _ctx.GraphSaveDir) : null;
        await session.SendAsync(JsonSerializer.Serialize(
            new LoadGraphResponse { Success = graph != null, Graph = graph },
            ServerJsonContext.Default.LoadGraphResponse));
    }
}
