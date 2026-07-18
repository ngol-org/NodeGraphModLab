using System.Text.Json;
using System.Linq;
using NodeGraphModLab.Core.Engine;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class GetNodeListHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "get_node_list";

    public GetNodeListHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var nodes = _ctx.Registry.GetAll().Select(d =>
        {
            _ctx.ScriptNodeId.TryGetValue(d.Id, out var filePath);
            string? lastModified = null;
            if (filePath != null && File.Exists(filePath))
            {
                lastModified = File.GetLastWriteTimeUtc(filePath).ToString("o");
            }
            return new NodeTypeInfo
            {
                Id = d.Id,
                Category = d.Category,
                DisplayName = d.DisplayName,
                Description = d.Description,
                NodeVersion = d.Version,
                FilePath = filePath,
                LastModified = lastModified,
                Ports = d.Ports.Select(p => new PortInfo
                {
                    Name = p.Name,
                    Direction = p.Direction.ToString().ToLower(),
                    DataType = p.DataType,
                    IsRequired = p.IsRequired,
                    Description = p.Description,
                    ShowInlineEditor = p.ShowInlineEditor
                }).ToList(),
                CustomWebUi = d.CustomWebUi
            };
        }).ToList();

        var response = new NodeListResponse { Nodes = nodes };
        await session.SendAsync(JsonSerializer.Serialize(response, ServerJsonContext.Default.NodeListResponse));
    }
}
