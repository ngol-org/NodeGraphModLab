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

        // 拡張の反映が主な用途なので既定は全タブ。
        var target = BrowserTarget.Read(root, defaultTarget: "all");

        var targets = await _ctx.SendReloadToBrowsers(preserveState, target);
        await session.SendAsync(JsonSerializer.Serialize(
            new ReloadWebUiResponse
            {
                Delivered = targets.Count,
                PreserveState = preserveState,
                Target = target,
                Targets = targets,
            },
            ServerJsonContext.Default.ReloadWebUiResponse));
    }
}
