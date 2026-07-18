using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// WebUI から永続ノードへ Execute なしでライブパラメータをマージする。
/// params は部分マージ（プリミティブ値のみ）。成功時はマージしたキー一覧を返す。
/// </summary>
internal sealed class PushNodeLiveParamsHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "push_node_live_params";

    public PushNodeLiveParamsHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var nodeId = root.TryGetProperty("nodeInstanceId", out var nid) ? nid.GetString() : null;
        if (string.IsNullOrEmpty(nodeId)) return;
        if (!root.TryGetProperty("params", out var paramsEl) || paramsEl.ValueKind != JsonValueKind.Object)
        {
            await SendResponseAsync(session, nodeId!, false, null, "missing_params");
            return;
        }

        var toMerge = new Dictionary<string, object?>();
        foreach (var prop in paramsEl.EnumerateObject())
        {
            if (!TryParsePrimitive(prop.Value, out var value))
            {
                await SendResponseAsync(session, nodeId!, false, null, "unsupported_type");
                return;
            }
            toMerge[prop.Name] = value;
        }

        if (toMerge.Count == 0)
        {
            await SendResponseAsync(session, nodeId!, false, null, "no_valid_params");
            return;
        }

        var mergedKeys = _ctx.LiveParamStore.MergeParams(nodeId!, toMerge);
        await SendResponseAsync(session, nodeId!, true, mergedKeys, null);
    }

    private static bool TryParsePrimitive(JsonElement el, out object? value)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String: value = el.GetString(); return true;
            case JsonValueKind.Number: value = el.GetDouble(); return true;
            case JsonValueKind.True:   value = true;  return true;
            case JsonValueKind.False:  value = false; return true;
            case JsonValueKind.Null:   value = null;  return true;
            default:
                value = null;
                return false;
        }
    }

    private static async Task SendResponseAsync(
        ISession session, string nodeId, bool success, IReadOnlyList<string>? mergedKeys, string? reason)
    {
        var resp = new PushNodeLiveParamsResponse
        {
            Success = success,
            NodeInstanceId = nodeId,
            MergedKeys = mergedKeys?.ToList() ?? new List<string>(),
            Reason = reason
        };
        await session.SendAsync(JsonSerializer.Serialize(resp, ServerJsonContext.Default.PushNodeLiveParamsResponse));
    }
}
