using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// ブラウザが返してきたキャンバスを、待っている要求へ引き渡す。
/// 応答は返さない（要求元は別のセッションで、そちらへは要求側のハンドラが返す）。
/// </summary>
internal sealed class CanvasGraphResultHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "canvas_graph_result";

    public CanvasGraphResultHandler(HandlerContext ctx) { _ctx = ctx; }

    public Task HandleAsync(ISession session, JsonElement root)
    {
        var token = root.TryGetProperty("requestToken", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;
        if (string.IsNullOrEmpty(token)) return Task.CompletedTask;

        var graph = root.TryGetProperty("graph", out var g) && g.ValueKind == JsonValueKind.Object
            ? NodeGraph.FromJson(g.GetRawText())
            : null;

        if (!_ctx.CompleteCanvasRequest(token!, graph))
        {
            // 期限切れ後に届いた応答。捨ててよいが、遅延の兆候として残す。
            _ctx.Log?.LogDebug($"[GraphServer] 待ち合わせの無いキャンバス応答を受信しました token={token}");
        }
        return Task.CompletedTask;
    }
}
