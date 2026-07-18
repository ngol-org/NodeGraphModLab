using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class DeleteGraphHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "delete_graph";

    public DeleteGraphHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var id = root.GetProperty("id").GetString();
        var ok = id != null && GraphPersistenceHelper.TryDelete(id, _ctx.GraphSaveDir);
        await session.SendAsync(JsonSerializer.Serialize(
            new DeleteGraphResponse { Success = ok },
            ServerJsonContext.Default.DeleteGraphResponse));
    }
}
