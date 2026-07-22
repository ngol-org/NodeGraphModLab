using System;
using System.Collections.Generic;
using System.Linq;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Core.Engine;

/// <summary>永続ノードのメタ情報。WebUI 表示用。</summary>
public sealed class NodeMeta
{
    public string NodeInstanceId { get; }
    public string DisplayName { get; }
    public string GraphName { get; }

    public NodeMeta(string nodeInstanceId, string displayName, string graphName)
    {
        NodeInstanceId = nodeInstanceId;
        DisplayName = displayName;
        GraphName = graphName;
    }
}

public sealed class PersistentNodeRunner
{
    private readonly List<PersistentRegistration> _registrations = new();
    private readonly object _lock = new();

    /// <summary>アクティブな登録セットが変化したときに発火。引数は現在アクティブなノードメタ情報一覧。</summary>
    public Action<IReadOnlyList<NodeMeta>>? OnChanged { get; set; }

    /// <summary>永続ノードJob・実行Jobを追跡するストア。Register() 呼び出し毎に自動でJobを発行する。</summary>
    public JobManager Jobs { get; } = new();

    public int Count
    {
        get { lock (_lock) return _registrations.Count; }
    }

    public IReadOnlyList<NodeMeta> GetActiveNodes()
    {
        lock (_lock)
            return _registrations
                .Where(r => r.IsActive)
                .GroupBy(r => r.NodeInstanceId)
                .Select(g => new NodeMeta(g.Key, g.First().DisplayName, g.First().GraphName))
                .ToList();
    }

    public IPersistentRegistration Register(string nodeInstanceId, string displayName, string graphName, PersistentCallbacks callbacks)
    {
        var job = Jobs.Create(JobKind.Persistent, nodeInstanceId);
        PersistentRegistration? reg = null; // OnStart/OnUpdate 内から Cancel() を呼べるよう前方参照で保持

        var jobCallbacks = new JobTrackingCallbacks(callbacks, job, () => reg)
        {
            OnStart = () =>
            {
                try { callbacks.OnStart?.Invoke(); job.SetRunning(); }
                catch (Exception ex) { job.Fail(ex.Message); reg?.Cancel(); }
            },
            OnUpdate = () =>
            {
                try { callbacks.OnUpdate?.Invoke(); }
                catch (Exception ex) { job.Fail(ex.Message); reg?.Cancel(); }
            },
            OnStop = () =>
            {
                try
                {
                    callbacks.OnStop?.Invoke();
                    if (job.State is JobState.Pending or JobState.Running) job.Complete();
                }
                catch (Exception ex) { job.Fail($"OnStop failed: {ex.Message}"); }
            },
        };

        reg = new PersistentRegistration(nodeInstanceId, displayName, graphName, jobCallbacks, job);
        lock (_lock)
        {
            _registrations.Add(reg);
        }
        OnChanged?.Invoke(GetActiveNodes());
        return reg;
    }

    /// <summary>
    /// PersistentCallbacks のラッパー。ホスト固有フェーズ（Unityの OnGUI 等）を持つサブクラスが渡された場合でも、
    /// 元の callbacks インスタンスを保持しつつ GetPhase を委譲することでオーバーライドを壊さない。
    /// OnUpdate と同じ「毎フレーム系」コールバックの例外を fail-fast（Job=Failed + 自動Cancel）にする。
    /// </summary>
    private sealed class JobTrackingCallbacks : PersistentCallbacks
    {
        private readonly PersistentCallbacks _inner;
        private readonly JobRecord _job;
        private readonly Func<PersistentRegistration?> _getReg;

        public JobTrackingCallbacks(PersistentCallbacks inner, JobRecord job, Func<PersistentRegistration?> getReg)
        {
            _inner = inner;
            _job = job;
            _getReg = getReg;
        }

        public override Action? GetPhase(string phaseName)
        {
            var phase = _inner.GetPhase(phaseName);
            if (phase == null) return null;
            return () =>
            {
                try { phase(); }
                catch (Exception ex) { _job.Fail(ex.Message); _getReg()?.Cancel(); }
            };
        }
    }

    public void CancelByNodeId(string nodeInstanceId)
    {
        lock (_lock)
        {
            foreach (var r in _registrations.Where(r => r.NodeInstanceId == nodeInstanceId))
                r.Cancel();
            // RemoveAll はここでは行わない。
            // DrainUpdate() がメインスレッドで FireStop() → RemoveAll を処理する。
            // GetActiveNodes() は IsActive で既にフィルタするため、呼び出し元への影響なし。
        }
        OnChanged?.Invoke(GetActiveNodes());
    }

    public void ClearAll()
    {
        List<PersistentRegistration> snapshot;
        lock (_lock)
        {
            snapshot = new List<PersistentRegistration>(_registrations);
            foreach (var r in _registrations) r.Cancel();
            _registrations.Clear();
        }
        // OnStop をロック外・呼び出し元スレッド（OnDestroy = メインスレッド）から呼ぶ
        foreach (var r in snapshot) r.FireStop();
        OnChanged?.Invoke(Array.Empty<NodeMeta>());
    }

    private void Drain(Func<PersistentCallbacks, Action?> selector, bool pruneInactive = false)
    {
        List<PersistentRegistration> snapshot;
        lock (_lock) { snapshot = new List<PersistentRegistration>(_registrations); }

        foreach (var reg in snapshot)
        {
            if (!reg.IsActive) continue;
            reg.FireStartIfNeeded();
            try { selector(reg.Callbacks)?.Invoke(); } catch { }
        }

        if (pruneInactive)
        {
            List<PersistentRegistration> toStop;
            int before, after;
            lock (_lock)
            {
                before = _registrations.Count;
                toStop = _registrations.Where(r => !r.IsActive).ToList();
                _registrations.RemoveAll(r => !r.IsActive);
                after = _registrations.Count;
            }
            // OnStop をロック外・メインスレッドから呼ぶ（ホスト側オブジェクト操作が安全）
            foreach (var r in toStop) r.FireStop();
            if (after < before) OnChanged?.Invoke(GetActiveNodes());
        }
    }

    public void DrainUpdate() => Drain(c => c.OnUpdate, pruneInactive: true);

    /// <summary>ホスト固有の拡張フェーズ（例: "Unity.OnGUI"）を排出する。<see cref="PersistentCallbacks.GetPhase"/> 参照。</summary>
    public void DrainPhase(string phaseName) => Drain(c => c.GetPhase(phaseName));

    internal sealed class PersistentRegistration : IPersistentRegistration
    {
        private bool _startFired;
        private bool _stopFired;

        public string NodeInstanceId { get; }
        public string DisplayName { get; }
        public string GraphName { get; }
        public bool IsActive { get; private set; } = true;
        public PersistentCallbacks Callbacks { get; }
        private readonly JobRecord? _job;

        public PersistentRegistration(string nodeInstanceId, string displayName, string graphName, PersistentCallbacks callbacks, JobRecord? job = null)
        {
            NodeInstanceId = nodeInstanceId;
            DisplayName = displayName;
            GraphName = graphName;
            Callbacks = callbacks;
            _job = job;
        }

        /// <summary>Jobの状況メッセージ（自由記述）を更新する。Register()経由でない場合(Job未紐付け)はno-op。</summary>
        public void ReportProgress(string message) => _job?.ReportProgress(message);

        public void Cancel()
        {
            IsActive = false;
            // OnStop はここで呼ばない。背景スレッドから呼ばれる場合があるため、
            // メインスレッド保証のある DrainUpdate / ClearAll で FireStop() を呼ぶ。
        }

        /// <summary>
        /// OnStop コールバックを一度だけ呼ぶ。
        /// PersistentNodeRunner がホストのメインスレッドから呼び出す。
        /// </summary>
        internal void FireStartIfNeeded()
        {
            if (_startFired) return;
            _startFired = true;
            try { Callbacks.OnStart?.Invoke(); } catch { }
        }

        internal void FireStop()
        {
            if (_stopFired) return;
            _stopFired = true;
            try { Callbacks.OnStop?.Invoke(); } catch { }
        }

        public void Dispose()
        {
            Cancel();
        }
    }
}
