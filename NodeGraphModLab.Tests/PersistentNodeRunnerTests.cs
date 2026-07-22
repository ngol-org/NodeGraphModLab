using NUnit.Framework;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class PersistentNodeRunnerTests
{
    private static IPersistentRegistration RegisterNode(
        PersistentNodeRunner runner,
        string nodeId = "node-1",
        Action? onUpdate = null,
        Action? onStop = null)
        => runner.Register(nodeId, "Test Node", "Test Graph",
            new PersistentCallbacks { OnUpdate = onUpdate, OnStop = onStop });

    /// <summary>ホスト固有拡張フェーズ（GetPhase オーバーライド）の動作検証用。</summary>
    private sealed class TestPhaseCallbacks : PersistentCallbacks
    {
        public Action? OnCustomPhase { get; init; }
        public override Action? GetPhase(string phaseName) => phaseName == "Test.CustomPhase" ? OnCustomPhase : null;
    }

    // ---- OnStop: DrainUpdate 経由 ----

    [Test]
    public void OnStop_CalledOnDrainUpdate_AfterCancel()
    {
        var runner = new PersistentNodeRunner();
        var stopCount = 0;
        var reg = RegisterNode(runner, onStop: () => stopCount++);

        reg.Cancel();
        Assert.That(stopCount, Is.EqualTo(0), "Cancel() 直後はまだ OnStop を呼ばない");

        runner.DrainUpdate();
        Assert.That(stopCount, Is.EqualTo(1), "DrainUpdate() で OnStop が 1 回呼ばれる");
    }

    [Test]
    public void OnStop_CalledExactlyOnce_OnMultipleDrains()
    {
        var runner = new PersistentNodeRunner();
        var stopCount = 0;
        var reg = RegisterNode(runner, onStop: () => stopCount++);

        reg.Cancel();
        runner.DrainUpdate();
        runner.DrainUpdate();
        runner.DrainUpdate();

        Assert.That(stopCount, Is.EqualTo(1), "複数回 Drain しても OnStop は 1 回のみ");
    }

    [Test]
    public void OnStop_NotCalled_WhileStillActive()
    {
        var runner = new PersistentNodeRunner();
        var stopCount = 0;
        RegisterNode(runner, onStop: () => stopCount++);

        runner.DrainUpdate();
        runner.DrainUpdate();

        Assert.That(stopCount, Is.EqualTo(0), "アクティブな間は OnStop を呼ばない");
    }

    // ---- OnStop: CancelByNodeId 経由 ----

    [Test]
    public void OnStop_CalledViaCancelByNodeId_OnNextDrain()
    {
        var runner = new PersistentNodeRunner();
        var stopCount = 0;
        RegisterNode(runner, nodeId: "node-abc", onStop: () => stopCount++);

        runner.CancelByNodeId("node-abc");
        Assert.That(stopCount, Is.EqualTo(0), "CancelByNodeId 直後はまだ OnStop を呼ばない");

        runner.DrainUpdate();
        Assert.That(stopCount, Is.EqualTo(1), "DrainUpdate() で OnStop が呼ばれる");
    }

    // ---- OnStop: ClearAll 経由 ----

    [Test]
    public void OnStop_CalledOnClearAll()
    {
        var runner = new PersistentNodeRunner();
        var stopCount = 0;
        RegisterNode(runner, onStop: () => stopCount++);

        runner.ClearAll();
        Assert.That(stopCount, Is.EqualTo(1), "ClearAll() で OnStop が呼ばれる");
    }

    [Test]
    public void OnStop_CalledForAllNodes_OnClearAll()
    {
        var runner = new PersistentNodeRunner();
        var stopCount = 0;
        RegisterNode(runner, nodeId: "node-1", onStop: () => stopCount++);
        RegisterNode(runner, nodeId: "node-2", onStop: () => stopCount++);
        RegisterNode(runner, nodeId: "node-3", onStop: () => stopCount++);

        runner.ClearAll();
        Assert.That(stopCount, Is.EqualTo(3), "ClearAll() で全ノードの OnStop が呼ばれる");
    }

    [Test]
    public void OnStop_NotCalledTwice_AfterCancelThenClearAll()
    {
        var runner = new PersistentNodeRunner();
        var stopCount = 0;
        var reg = RegisterNode(runner, onStop: () => stopCount++);

        reg.Cancel();
        runner.DrainUpdate(); // ← ここで FireStop()
        runner.ClearAll();    // ← すでに除去済みなので呼ばれない

        Assert.That(stopCount, Is.EqualTo(1), "DrainUpdate 後の ClearAll で二重呼び出ししない");
    }

    // ---- OnStart ----

    [Test]
    public void OnStart_CalledOnFirstDrain_BeforeOnUpdate()
    {
        var runner = new PersistentNodeRunner();
        var order = new List<string>();
        runner.Register("n1", "N", "G", new PersistentCallbacks
        {
            OnStart  = () => order.Add("start"),
            OnUpdate = () => order.Add("update"),
        });

        runner.DrainUpdate();

        Assert.That(order, Is.EqualTo(new[] { "start", "update" }),
            "OnStart は OnUpdate より前に呼ばれる");
    }

    [Test]
    public void OnStart_CalledExactlyOnce_AcrossMultipleDrains()
    {
        var runner = new PersistentNodeRunner();
        var startCount = 0;
        runner.Register("n1", "N", "G", new PersistentCallbacks
        {
            OnStart  = () => startCount++,
            OnUpdate = () => { },
        });

        runner.DrainUpdate();
        runner.DrainUpdate();
        runner.DrainUpdate();

        Assert.That(startCount, Is.EqualTo(1), "OnStart は複数 Drain しても 1 回のみ");
    }

    [Test]
    public void OnStart_CalledOnFirstDrain_EvenIfOnlyExtensionPhase()
    {
        var runner = new PersistentNodeRunner();
        var order = new List<string>();
        runner.Register("n1", "N", "G", new TestPhaseCallbacks
        {
            OnStart = () => order.Add("start"),
            OnCustomPhase = () => order.Add("phase"),
        });

        runner.DrainPhase("Test.CustomPhase");

        Assert.That(order, Is.EqualTo(new[] { "start", "phase" }),
            "拡張フェーズ（DrainPhase）が最初に呼ばれた場合も OnStart が先に来る");
    }

    [Test]
    public void DrainPhase_UnknownPhaseName_ReturnsNullAndDoesNothing()
    {
        var runner = new PersistentNodeRunner();
        var called = false;
        runner.Register("n1", "N", "G", new TestPhaseCallbacks { OnCustomPhase = () => called = true });

        runner.DrainPhase("Unknown.Phase");

        Assert.That(called, Is.False, "未対応のフェーズ名では何も発火しない");
    }

    [Test]
    public void OnStart_NoException_WhenNull()
    {
        var runner = new PersistentNodeRunner();
        runner.Register("n1", "N", "G", new PersistentCallbacks
        {
            OnStart  = null,
            OnUpdate = () => { },
        });
        Assert.DoesNotThrow(() => runner.DrainUpdate());
    }

    [Test]
    public void OnStart_ExceptionInCallback_DoesNotPreventOnUpdate()
    {
        var runner = new PersistentNodeRunner();
        var updateCalled = false;
        runner.Register("n1", "N", "G", new PersistentCallbacks
        {
            OnStart  = () => throw new InvalidOperationException("start error"),
            OnUpdate = () => updateCalled = true,
        });

        runner.DrainUpdate();

        Assert.That(updateCalled, Is.True, "OnStart 例外後も OnUpdate は呼ばれる");
    }

    [Test]
    public void OnStart_NotCalledAfterStop()
    {
        var runner = new PersistentNodeRunner();
        var startCount = 0;
        var reg = runner.Register("n1", "N", "G", new PersistentCallbacks
        {
            OnStart = () => startCount++,
        });

        reg.Cancel();
        runner.DrainUpdate(); // OnStart は呼ばれない（IsActive=false でスキップ）

        Assert.That(startCount, Is.EqualTo(0), "Cancel 後は OnStart も呼ばれない");
    }

    // ---- OnStop なしでも例外が出ないこと ----

    [Test]
    public void DrainUpdate_NoException_WhenOnStopIsNull()
    {
        var runner = new PersistentNodeRunner();
        var reg = RegisterNode(runner, onStop: null);

        reg.Cancel();
        Assert.DoesNotThrow(() => runner.DrainUpdate());
    }

    [Test]
    public void ClearAll_NoException_WhenOnStopIsNull()
    {
        var runner = new PersistentNodeRunner();
        RegisterNode(runner, onStop: null);

        Assert.DoesNotThrow(() => runner.ClearAll());
    }

    // ---- OnStop 内で例外が出ても他ノードに影響しないこと ----

    [Test]
    public void OnStop_ExceptionInCallback_DoesNotAffectOtherNodes()
    {
        var runner = new PersistentNodeRunner();
        var secondStopped = false;

        RegisterNode(runner, nodeId: "node-throw", onStop: () => throw new InvalidOperationException("test"));
        RegisterNode(runner, nodeId: "node-ok",    onStop: () => secondStopped = true);

        runner.ClearAll();
        Assert.That(secondStopped, Is.True, "例外ノードの次のノードの OnStop も呼ばれる");
    }

    // ---- Job（永続ノードJob自動発行・fail-fast） ----

    private static JobSnapshot GetJob(PersistentNodeRunner runner, string nodeId)
        => runner.Jobs.GetJobsForNodes(new[] { nodeId }, DateTime.MinValue).Single();

    [Test]
    public void Job_CreatedPending_ThenRunningAfterFirstDrain()
    {
        var runner = new PersistentNodeRunner();
        RegisterNode(runner, nodeId: "n1");

        Assert.That(GetJob(runner, "n1").State, Is.EqualTo(JobState.Pending));

        runner.DrainUpdate();

        Assert.That(GetJob(runner, "n1").State, Is.EqualTo(JobState.Running));
    }

    [Test]
    public void Job_NormalCompletion_MarksCompleted()
    {
        var runner = new PersistentNodeRunner();
        var reg = RegisterNode(runner, nodeId: "n1");

        runner.DrainUpdate();
        reg.Cancel();
        runner.DrainUpdate(); // FireStop → Job.Complete()

        Assert.That(GetJob(runner, "n1").State, Is.EqualTo(JobState.Completed));
    }

    [Test]
    public void Job_OnUpdateException_MarksFailedAndStopsFromNextTick()
    {
        var runner = new PersistentNodeRunner();
        var updateCount = 0;
        runner.Register("n1", "N", "G", new PersistentCallbacks
        {
            OnUpdate = () => { updateCount++; throw new InvalidOperationException("boom"); },
        });

        runner.DrainUpdate(); // 例外発生 → Job=Failed + 自動Cancel（この tick 内の OnUpdate 自体は実行済み）

        var job = GetJob(runner, "n1");
        Assert.That(job.State, Is.EqualTo(JobState.Failed));
        Assert.That(job.Message, Does.Contain("boom"));

        runner.DrainUpdate(); // 次tickでは pruneInactive によりノードは既に除去され、OnUpdate は呼ばれない
        Assert.That(updateCount, Is.EqualTo(1), "自動Cancel後、次のtickからはOnUpdateが呼ばれない");
    }

    [Test]
    public void Job_OnStartException_MarksFailed()
    {
        var runner = new PersistentNodeRunner();
        runner.Register("n1", "N", "G", new PersistentCallbacks
        {
            OnStart = () => throw new InvalidOperationException("start error"),
            OnUpdate = () => { },
        });

        runner.DrainUpdate();

        Assert.That(GetJob(runner, "n1").State, Is.EqualTo(JobState.Failed));
    }

    [Test]
    public void Job_GetPhaseException_MarksFailedAndStopsFromNextTick()
    {
        var runner = new PersistentNodeRunner();
        var phaseCount = 0;
        runner.Register("n1", "N", "G", new TestPhaseCallbacks
        {
            OnCustomPhase = () => { phaseCount++; throw new InvalidOperationException("phase boom"); },
        });

        runner.DrainPhase("Test.CustomPhase");
        Assert.That(GetJob(runner, "n1").State, Is.EqualTo(JobState.Failed));

        // DrainPhase は pruneInactive:false のため、実際の除去は次の DrainUpdate() まで反映される
        runner.DrainUpdate();
        runner.DrainPhase("Test.CustomPhase");
        Assert.That(phaseCount, Is.EqualTo(1), "自動Cancel後はフェーズコールバックも呼ばれない");
    }

    [Test]
    public void Job_OnStopException_MarksFailedWithMessage()
    {
        var runner = new PersistentNodeRunner();
        var reg = RegisterNode(runner, nodeId: "n1", onStop: () => throw new InvalidOperationException("stop boom"));

        runner.DrainUpdate();
        reg.Cancel();
        runner.DrainUpdate();

        var job = GetJob(runner, "n1");
        Assert.That(job.State, Is.EqualTo(JobState.Failed));
        Assert.That(job.Message, Does.Contain("stop boom"));
    }

    [Test]
    public void Job_ReportProgress_UpdatesMessageWithoutChangingState()
    {
        var runner = new PersistentNodeRunner();
        IPersistentRegistration? reg = null;
        reg = runner.Register("n1", "N", "G", new PersistentCallbacks
        {
            OnUpdate = () => reg!.ReportProgress("50/100 done"),
        });

        runner.DrainUpdate();

        var job = GetJob(runner, "n1");
        Assert.That(job.State, Is.EqualTo(JobState.Running), "ReportProgressはStateを変更しない");
        Assert.That(job.Message, Is.EqualTo("50/100 done"));
    }
}
