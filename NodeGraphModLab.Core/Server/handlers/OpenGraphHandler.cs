using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class OpenGraphHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "open_graph";

    public OpenGraphHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var success = !string.IsNullOrEmpty(id);
        var delivered = success && await _ctx.SendOpenGraphToLatestBrowser(id!);
        await session.SendAsync(JsonSerializer.Serialize(
            new OpenGraphResponse { Success = success, Delivered = delivered, GraphId = id },
            ServerJsonContext.Default.OpenGraphResponse));
    }
}
