using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NodeGraphModLab.Core.Engine;

/// <summary>Jobの状態。Pending→Running→Completed/Failed の一方向遷移。</summary>
public enum JobState { Pending, Running, Completed, Failed }

/// <summary>Jobの種別。永続ノードのライフサイクル追跡か、グラフ実行全体の非同期追跡か。</summary>
public enum JobKind { Persistent, Execution }

/// <summary>
/// 呼び出し側に返す不変スナップショット。JobRecord の可変状態への参照を外部に漏らさないための値コピー。
/// </summary>
public readonly struct JobSnapshot
{
    public string JobId { get; }
    public JobKind Kind { get; }
    public string NodeInstanceId { get; }
    public JobState State { get; }
    public string? Message { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; }

    public JobSnapshot(string jobId, JobKind kind, string nodeInstanceId, JobState state,
        string? message, DateTime createdAtUtc, DateTime updatedAtUtc)
    {
        JobId = jobId;
        Kind = kind;
        NodeInstanceId = nodeInstanceId;
        State = state;
        Message = message;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }
}

/// <summary>
/// Jobの可変状態を保持するレコード。State/Message の読み書きは内部ロックで保護する。
/// 外部への公開は必ず <see cref="GetSnapshot"/> の値コピー経由で行う。
/// </summary>
public sealed class JobRecord
{
    private readonly object _lock = new();
    private JobState _state = JobState.Pending;

    /// <summary>進捗メモ・完了メモ・失敗理由をまとめた自由記述フィールド。オプトインのため既定は null。</summary>
    private string? _message;
    private DateTime _updatedAtUtc;

    public string JobId { get; }
    public JobKind Kind { get; }
    public string NodeInstanceId { get; }
    public DateTime CreatedAtUtc { get; }

    /// <summary>現在の状態をロック付きで読む（Pending/Running/Completed/Failedの分岐判定用）。</summary>
    public JobState State { get { lock (_lock) return _state; } }

    public JobRecord(string jobId, JobKind kind, string nodeInstanceId)
    {
        JobId = jobId;
        Kind = kind;
        NodeInstanceId = nodeInstanceId;
        CreatedAtUtc = DateTime.UtcNow;
        _updatedAtUtc = CreatedAtUtc;
    }

    public void SetRunning()
    {
        lock (_lock) { _state = JobState.Running; _updatedAtUtc = DateTime.UtcNow; }
    }

    /// <summary>Stateには触れず、自由記述メッセージだけ更新する（ノードのオプトイン進捗報告用）。</summary>
    public void ReportProgress(string message)
    {
        lock (_lock) { _message = message; _updatedAtUtc = DateTime.UtcNow; }
    }

    /// <summary>message省略時は直近の ReportProgress 内容をそのまま結果メモとして残す。</summary>
    public void Complete(string? message = null)
    {
        lock (_lock)
        {
            _state = JobState.Completed;
            if (message != null) _message = message;
            _updatedAtUtc = DateTime.UtcNow;
        }
    }

    public void Fail(string message)
    {
        lock (_lock) { _state = JobState.Failed; _message = message; _updatedAtUtc = DateTime.UtcNow; }
    }

    public JobSnapshot GetSnapshot()
    {
        lock (_lock)
            return new JobSnapshot(JobId, Kind, NodeInstanceId, _state, _message, CreatedAtUtc, _updatedAtUtc);
    }
}

/// <summary>
/// 永続ノードJob・実行Jobを統一的に管理するスレッドセーフなストア。
/// 生成・削除は ConcurrentDictionary に委譲し、個々の Job の状態更新は JobRecord 内部ロックで保護する。
/// </summary>
public sealed class JobManager
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();

    public JobRecord Create(JobKind kind, string nodeInstanceId)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var record = new JobRecord(jobId, kind, nodeInstanceId);
        _jobs[jobId] = record;
        return record;
    }

    public JobSnapshot? Get(string jobId)
    {
        return _jobs.TryGetValue(jobId, out var record) ? record.GetSnapshot() : null;
    }

    /// <summary>指定ノードID集合に属し、指定時刻以降に作成されたJobを返す（execute_graph/run_nodeレスポンスへの付与用）。</summary>
    public IReadOnlyList<JobSnapshot> GetJobsForNodes(IEnumerable<string> nodeInstanceIds, DateTime sinceUtc)
    {
        var idSet = new HashSet<string>(nodeInstanceIds);
        if (idSet.Count == 0) return Array.Empty<JobSnapshot>();

        return _jobs.Values
            .Where(r => r.CreatedAtUtc >= sinceUtc && idSet.Contains(r.NodeInstanceId))
            .Select(r => r.GetSnapshot())
            .ToList();
    }

    /// <summary>Completed/FailedのままTtlを超えたJobを間引く。既存のTickの流れに便乗して呼ぶ想定。</summary>
    public void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _jobs)
        {
            var snap = kv.Value.GetSnapshot();
            if ((snap.State == JobState.Completed || snap.State == JobState.Failed) && now - snap.UpdatedAtUtc > Ttl)
                _jobs.TryRemove(kv.Key, out _);
        }
    }
}
