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

        // 既定は最新1タブ。従来の挙動を変えないため。
        var target = BrowserTarget.Read(root, defaultTarget: "latest");

        var targets = success
            ? await _ctx.SendOpenGraphToBrowsers(id!, target)
            : new List<BrowserTargetInfo>();

        await session.SendAsync(JsonSerializer.Serialize(
            new OpenGraphResponse
            {
                Success = success,
                Delivered = targets.Count > 0,
                GraphId = id,
                Targets = targets,
            },
            ServerJsonContext.Default.OpenGraphResponse));
    }
}
