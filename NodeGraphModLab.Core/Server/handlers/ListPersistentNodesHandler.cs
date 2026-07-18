using System.Linq;
using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ListPersistentNodesHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "list_persistent_nodes";

    public ListPersistentNodesHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var nodes = _ctx.Runner.GetActiveNodes()
            .Select(m => new PersistentNodeInfo
            {
                NodeInstanceId = m.NodeInstanceId,
                DisplayName    = m.DisplayName,
                GraphName      = m.GraphName,
            })
            .ToList();
        await session.SendAsync(JsonSerializer.Serialize(
            new ListPersistentNodesResponse { Nodes = nodes },
            ServerJsonContext.Default.ListPersistentNodesResponse));
    }
}
