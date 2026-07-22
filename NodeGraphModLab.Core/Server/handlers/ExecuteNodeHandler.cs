using System.Text.Json;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// グラフを使わずに単一ノードを実行し、出力ポート値を返す。
/// 参照型の出力は SnapshotStore に格納し $snapshot ハンドルとして返す。
/// </summary>
internal sealed class ExecuteNodeHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "execute_node";

    public ExecuteNodeHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        if (!root.TryGetProperty("nodeTypeId", out var typeIdElem))
        {
            await session.SendAsync(JsonSerializer.Serialize(
                new ErrorResponse { Message = "Missing nodeTypeId" },
                ServerJsonContext.Default.ErrorResponse));
            return;
        }

        var nodeTypeId = typeIdElem.GetString() ?? "";
        root.TryGetProperty("inputs", out var inputsElem);

        var (paramValues, inputValues) = ResolveInputs(inputsElem, session.SnapshotStore);
        var isAsync = root.TryGetProperty("async", out var a) && a.ValueKind == JsonValueKind.True;
        var executionStartUtc = DateTime.UtcNow;

        JobRecord? execJob = null;
        if (isAsync)
        {
            execJob = _ctx.Runner.Jobs.Create(JobKind.Execution, "$run_node");
            await session.SendAsync(JsonSerializer.Serialize(
                new JobStartedResponse { JobId = execJob.JobId },
                ServerJsonContext.Default.JobStartedResponse));
        }

        var runTask = Task.Run(async () =>
        {
            var baseCtx = new MainThreadExecutionContext(
                "engine", _ctx.Log!, _ctx.Runner, _ctx.Registry, _ctx.Store, _ctx.LiveParamStore, _ctx.ExtensionServices);

            var result = _ctx.Executor.ExecuteSingleNode(
                nodeTypeId, paramValues, inputValues, baseCtx);

            // 出力値をシリアライズ（参照型は SnapshotStore に格納して $snapshot ハンドルへ変換）
            var outputs = BuildOutputs(result.Outputs, session.SnapshotStore);

            var resp = new ExecuteNodeResponse
            {
                Success      = result.Success,
                ErrorMessage = result.ErrorMessage,
                DurationMs   = result.Duration.TotalMilliseconds,
                Outputs      = outputs,
                Logs         = result.Logs.Select(l => l.Message).ToList(),
                // run_node実行中に登録された永続ノードJobを収集（execute_graphと同格の優先度、async指定の有無に関わらず常時）
                Jobs         = _ctx.Runner.Jobs
                    .GetJobsForNodes(new[] { result.InstanceId }, executionStartUtc)
                    .Select(j => new JobRef { JobId = j.JobId, NodeInstanceId = j.NodeInstanceId })
                    .ToList(),
            };

            if (execJob != null)
            {
                var payload = JsonSerializer.Serialize(resp, ServerJsonContext.Default.ExecuteNodeResponse);
                if (result.Success) execJob.Complete(payload);
                else execJob.Fail(result.ErrorMessage ?? "unknown error");
            }

            await session.SendAsync(JsonSerializer.Serialize(
                resp, ServerJsonContext.Default.ExecuteNodeResponse));
        });

        if (!isAsync) await runTask; // 同期実行時は従来通り完了を待つ。async時はここで待たずバックグラウンド続行
    }

    /// <summary>
    /// inputs JSON を paramValues（JsonElement）と inputValues（object?）に分解する。
    /// { "$snapshot": "key" } 形式は SnapshotStore から実参照を取得して inputValues に注入する。
    /// </summary>
    private static (Dictionary<string, JsonElement> paramValues, Dictionary<string, object?> inputValues)
        ResolveInputs(JsonElement inputs, ISnapshotStore store)
    {
        var paramValues = new Dictionary<string, JsonElement>();
        var inputValues = new Dictionary<string, object?>();

        if (inputs.ValueKind != JsonValueKind.Object)
            return (paramValues, inputValues);

        foreach (var prop in inputs.EnumerateObject())
        {
            var portName = prop.Name;
            var val = prop.Value;

            // $snapshot ハンドル → SnapshotStore から実参照を取得
            if (val.ValueKind == JsonValueKind.Object &&
                val.TryGetProperty("$snapshot", out var snapshotKeyElem))
            {
                var key = snapshotKeyElem.GetString() ?? "";
                var parts = key.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                    inputValues[portName] = store.GetSnapshot(parts[0], parts[1]);
                continue;
            }

            // プリミティブ値: paramValues と inputValues 両方に設定
            // ノードは GetPortValue("x") ?? GetParam<T>("x") パターンで両方読める
            paramValues[portName] = val;
            inputValues[portName] = val.ValueKind switch
            {
                JsonValueKind.Number  => val.GetDouble(),
                JsonValueKind.String  => val.GetString(),
                JsonValueKind.True    => (object?)true,
                JsonValueKind.False   => false,
                JsonValueKind.Null    => null,
                _                    => val.GetRawText()
            };
        }

        return (paramValues, inputValues);
    }

    /// <summary>
    /// 出力値をシリアライズする。
    /// プリミティブ型は JSON 値として返す。
    /// 参照型は SnapshotStore に格納し { "$snapshot": "nodeId:portName" } ハンドルとして返す。
    /// </summary>
    private static Dictionary<string, JsonElement> BuildOutputs(
        Dictionary<string, object?> outputs, ISnapshotStore store)
    {
        var result = new Dictionary<string, JsonElement>();

        foreach (var (portName, value) in outputs)
        {
            result[portName] = ToJsonElement(value, portName, store);
        }

        return result;
    }

    private static JsonElement ToJsonElement(object? value, string portName, ISnapshotStore store)
    {
        switch (value)
        {
            case null:
                return JsonSerializer.SerializeToElement<object?>(null);
            case double d:
                return JsonSerializer.SerializeToElement(d);
            case float f:
                return JsonSerializer.SerializeToElement((double)f);
            case int i:
                return JsonSerializer.SerializeToElement((double)i);
            case long l:
                return JsonSerializer.SerializeToElement((double)l);
            case bool b:
                return JsonSerializer.SerializeToElement(b);
            case string s:
                return JsonSerializer.SerializeToElement(s);
            default:
                // 参照型: SnapshotStore に格納し $snapshot ハンドルを返す
                // instanceId は ExecuteSingleNode 内で "rn_..." として生成されるが、
                // ここでは portName をキーの一部に使う一意なノードIDを生成する
                var instanceId = $"rn_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                store.SetSnapshot(instanceId, portName, value);
                var handleJson = $"{{\"$snapshot\":\"{instanceId}:{portName}\"}}";
                return JsonSerializer.Deserialize<JsonElement>(handleJson);
        }
    }
}
