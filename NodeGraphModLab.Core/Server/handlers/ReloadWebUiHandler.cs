using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ReloadWebUiHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "reload_webui";

    public ReloadWebUiHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var preserveState = !root.TryGetProperty("preserveState", out var el)
            || el.ValueKind != JsonValueKind.False;

        var delivered = await _ctx.SendReloadToBrowsers(preserveState);
        await session.SendAsync(JsonSerializer.Serialize(
            new ReloadWebUiResponse { Delivered = delivered, PreserveState = preserveState },
            ServerJsonContext.Default.ReloadWebUiResponse));
    }
}
