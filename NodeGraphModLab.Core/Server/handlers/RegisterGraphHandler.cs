using System.IO;
using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// グラフを保存せずに一時登録し、その id を返す。返った id は
/// <c>open_graph</c> にそのまま渡せる（グラフを開く経路は id しか運ばないため）。
///
/// 試作のたびに保存領域へ実体が残るのを避けるための機構。
/// </summary>
internal sealed class RegisterGraphHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "register_graph";

    public RegisterGraphHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        NodeGraph? graph = null;
        if (root.TryGetProperty("graph", out var graphEl))
            graph = NodeGraph.FromJson(graphEl.GetRawText());

        if (graph == null)
        {
            await Respond(session, false, null, "invalid_graph");
            return;
        }

        var id = graph.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString();
            graph.Id = id;
        }
        else if (SavedGraphExists(id))
        {
            // 一時グラフが保存済みグラフを隠さないようにする。
            // 黙って別 id にすると、呼び出し側が指定した id で開こうとして食い違う。
            await Respond(session, false, id, "id_conflicts_with_saved_graph");
            return;
        }

        _ctx.TemporaryGraphs.Add(id, graph);
        await Respond(session, true, id, null);
    }

    private bool SavedGraphExists(string id)
    {
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return File.Exists(Path.Combine(_ctx.GraphSaveDir, id + ".json"));
    }

    private async Task Respond(ISession session, bool success, string? id, string? reason)
    {
        await session.SendAsync(JsonSerializer.Serialize(
            new RegisterGraphResponse
            {
                Success = success,
                Id = id,
                Reason = reason,
                Count = _ctx.TemporaryGraphs.Count,
            },
            ServerJsonContext.Default.RegisterGraphResponse));
    }
}
