using System.Text.Json;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ExecuteGraphHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "execute_graph";

    public ExecuteGraphHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var graph = NodeGraph.FromJson(root.GetProperty("graph").GetRawText());
        if (graph == null)
        {
            await session.SendAsync(JsonSerializer.Serialize(
                new ErrorResponse { Message = "Invalid graph payload" },
                ServerJsonContext.Default.ErrorResponse));
            return;
        }

        var isAsync = root.TryGetProperty("async", out var a) && a.ValueKind == JsonValueKind.True;
        if (isAsync)
        {
            var job = _ctx.Runner.Jobs.Create(JobKind.Execution, "$graph");
            _ctx.PendingExecutions.Enqueue(new PendingExecution(session, graph, job: job));
            await session.SendAsync(JsonSerializer.Serialize(
                new JobStartedResponse { JobId = job.JobId },
                ServerJsonContext.Default.JobStartedResponse));
        }
        else
        {
            _ctx.PendingExecutions.Enqueue(new PendingExecution(session, graph));
        }
    }
}
