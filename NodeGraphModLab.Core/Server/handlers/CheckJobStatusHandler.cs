using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// execute_graph系/run_node が返す jobId（永続ノードJobまたはasync実行Job）の状態をポーリングする。
/// </summary>
internal sealed class CheckJobStatusHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "check_job_status";

    public CheckJobStatusHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var jobId = root.TryGetProperty("jobId", out var idElem) ? idElem.GetString() ?? "" : "";
        var snap = _ctx.Runner.Jobs.Get(jobId);

        var resp = snap is { } s
            ? new JobStatusResponse
            {
                Found = true,
                JobId = s.JobId,
                Kind = s.Kind.ToString(),
                NodeInstanceId = s.NodeInstanceId,
                State = s.State.ToString(),
                Message = s.Message,
            }
            : new JobStatusResponse { Found = false, JobId = jobId };

        await session.SendAsync(JsonSerializer.Serialize(resp, ServerJsonContext.Default.JobStatusResponse));
    }
}
