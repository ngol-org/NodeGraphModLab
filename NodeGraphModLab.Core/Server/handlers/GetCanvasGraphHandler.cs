using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class GetCanvasGraphHandler : IMessageHandler
{
    /// <summary>
    /// ブラウザの応答を待つ上限。呼び出し側のタイムアウトより短くする必要がある。
    /// 先に呼び出し側が諦めると、理由の付いた応答ではなく無応答として扱われてしまう。
    /// </summary>
    private const int DefaultTimeoutMs = 8000;
    private const int MinTimeoutMs = 500;
    private const int MaxTimeoutMs = 60000;

    private readonly HandlerContext _ctx;
    public string MessageType => "get_canvas_graph";

    public GetCanvasGraphHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        // 「今のキャンバス」を1つ返す用途なので既定は最後に接続したタブ。
        var target = BrowserTarget.Read(root, defaultTarget: "latest");

        var timeoutMs = root.TryGetProperty("timeoutMs", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Min(MaxTimeoutMs, Math.Max(MinTimeoutMs, t.GetInt32()))
            : DefaultTimeoutMs;

        var ifNoneMatch = root.TryGetProperty("ifNoneMatch", out var inm) && inm.ValueKind == JsonValueKind.String
            ? inm.GetString()
            : null;

        var results = await _ctx.RequestCanvasFromBrowsers(target, timeoutMs, ifNoneMatch);
        // 本文が無くても Unchanged なら答えは得られている。応答が来なかった場合だけ失敗。
        var answered = results.Count(r => r.Graph != null || r.Unchanged);

        await session.SendAsync(JsonSerializer.Serialize(
            new GetCanvasGraphResponse
            {
                Success = answered > 0,
                Target = target,
                Asked = results.Count,
                Results = results,
                Reason = answered > 0 ? null
                    : results.Count == 0 ? "no_browser_connected" : "timeout",
            },
            ServerJsonContext.Default.GetCanvasGraphResponse));
    }
}
