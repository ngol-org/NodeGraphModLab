using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class SaveGraphHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "save_graph";

    public SaveGraphHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var graph = NodeGraph.FromJson(root.GetProperty("graph").GetRawText());
        var ok = graph != null && GraphPersistenceHelper.TrySave(graph, _ctx.GraphSaveDir, _ctx.Log);
        await session.SendAsync(JsonSerializer.Serialize(
            new SaveGraphResponse { Success = ok, Id = graph?.Id },
            ServerJsonContext.Default.SaveGraphResponse));
    }
}
