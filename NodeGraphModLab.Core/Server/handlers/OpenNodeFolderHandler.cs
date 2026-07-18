using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class OpenNodeFolderHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "open_node_folder";

    public OpenNodeFolderHandler(HandlerContext ctx) { _ctx = ctx; }

    public Task HandleAsync(ISession session, JsonElement root)
    {
        var nodeTypeId = root.TryGetProperty("nodeTypeId", out var nid) ? nid.GetString() : null;
        if (string.IsNullOrEmpty(nodeTypeId)) return Task.CompletedTask;

        if (!_ctx.ScriptNodeId.TryGetValue(nodeTypeId, out var filePath)) return Task.CompletedTask;
        if (!File.Exists(filePath)) return Task.CompletedTask;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = false
        });

        return Task.CompletedTask;
    }
}
