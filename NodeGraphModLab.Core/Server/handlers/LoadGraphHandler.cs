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
        // 一時登録を先に引く。保存済みと同じ id は登録時に拒否しているので、保存版が隠れることはない。
        var graph = id != null
            ? _ctx.TemporaryGraphs.TryGet(id) ?? GraphPersistenceHelper.TryLoad(id, _ctx.GraphSaveDir)
            : null;
        await session.SendAsync(JsonSerializer.Serialize(
            new LoadGraphResponse { Success = graph != null, Graph = graph },
            ServerJsonContext.Default.LoadGraphResponse));
    }
}
