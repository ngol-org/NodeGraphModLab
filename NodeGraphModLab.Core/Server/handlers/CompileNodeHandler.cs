using System.Text.Json;
using NodeGraphModLab.Core.Engine;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class CompileNodeHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "compile_node";

    public CompileNodeHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var source = root.TryGetProperty("source", out var s) ? s.GetString() : null;
        var className = root.TryGetProperty("className", out var c) ? c.GetString() : null;
        var persist = root.TryGetProperty("persist", out var p) && p.GetBoolean();

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(className))
        {
            var failResp = new CompileNodeResponse { Success = false, ErrorMessage = "source and className are required" };
            await session.SendAsync(JsonSerializer.Serialize(failResp, ServerJsonContext.Default.CompileNodeResponse));
            return;
        }

        CompileNodeResponse response;
        try
        {
            response = await RoslynCompiler.CompileAndRegisterAsync(
                source, className, _ctx.Registry, _ctx.Log,
                persist: persist,
                dynamicNodesDir: persist ? _ctx.DynamicNodesDir : null);
        }
        catch (Exception ex)
        {
            _ctx.Log.LogError($"[CompileNodeHandler] Unexpected error: {ex.Message}");
            response = new CompileNodeResponse { Success = false, ErrorMessage = $"Unexpected error: {ex.Message}" };
        }
        await session.SendAsync(JsonSerializer.Serialize(response, ServerJsonContext.Default.CompileNodeResponse));
    }
}
