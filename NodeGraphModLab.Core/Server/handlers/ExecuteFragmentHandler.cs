using System.Text.Json;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class ExecuteFragmentHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "execute_fragment";

    public ExecuteFragmentHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var graph = NodeGraph.FromJson(root.GetProperty("graph").GetRawText());
        var fragId = root.TryGetProperty("fragmentId", out var fid) ? fid.GetString() : null;
        var pinned = MessageHelper.ParseStringList(root, "pinnedFragmentIds");
        if (graph == null || fragId == null)
        {
            await session.SendAsync(JsonSerializer.Serialize(
                new ErrorResponse { Message = "Invalid execute_fragment payload" },
                ServerJsonContext.Default.ErrorResponse));
            return;
        }

        var isAsync = root.TryGetProperty("async", out var a) && a.ValueKind == JsonValueKind.True;
        if (isAsync)
        {
            var job = _ctx.Runner.Jobs.Create(JobKind.Execution, "$graph");
            _ctx.PendingExecutions.Enqueue(new PendingExecution(session, graph, fragmentId: fragId, pinnedIds: pinned, job: job));
            await session.SendAsync(JsonSerializer.Serialize(
                new JobStartedResponse { JobId = job.JobId },
                ServerJsonContext.Default.JobStartedResponse));
        }
        else
        {
            _ctx.PendingExecutions.Enqueue(new PendingExecution(session, graph, fragmentId: fragId, pinnedIds: pinned));
        }
    }
}
