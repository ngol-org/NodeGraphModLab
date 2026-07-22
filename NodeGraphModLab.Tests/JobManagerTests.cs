using NUnit.Framework;
using NodeGraphModLab.Core.Engine;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class JobManagerTests
{
    [Test]
    public void Create_StartsAsPending_WithNoMessage()
    {
        var mgr = new JobManager();
        var job = mgr.Create(JobKind.Persistent, "n1");

        var snap = mgr.Get(job.JobId)!.Value;
        Assert.That(snap.State, Is.EqualTo(JobState.Pending));
        Assert.That(snap.Message, Is.Null);
        Assert.That(snap.NodeInstanceId, Is.EqualTo("n1"));
        Assert.That(snap.Kind, Is.EqualTo(JobKind.Persistent));
    }

    [Test]
    public void SetRunning_TransitionsState()
    {
        var mgr = new JobManager();
        var job = mgr.Create(JobKind.Execution, "$graph");

        job.SetRunning();

        Assert.That(mgr.Get(job.JobId)!.Value.State, Is.EqualTo(JobState.Running));
    }

    [Test]
    public void ReportProgress_UpdatesMessage_WithoutChangingState()
    {
        var mgr = new JobManager();
        var job = mgr.Create(JobKind.Persistent, "n1");
        job.SetRunning();

        job.ReportProgress("340/1024 processed");

        var snap = mgr.Get(job.JobId)!.Value;
        Assert.That(snap.State, Is.EqualTo(JobState.Running));
        Assert.That(snap.Message, Is.EqualTo("340/1024 processed"));
    }

    [Test]
    public void Complete_WithoutMessage_KeepsLastReportedMessage()
    {
        var mgr = new JobManager();
        var job = mgr.Create(JobKind.Persistent, "n1");
        job.ReportProgress("last progress note");

        job.Complete();

        var snap = mgr.Get(job.JobId)!.Value;
        Assert.That(snap.State, Is.EqualTo(JobState.Completed));
        Assert.That(snap.Message, Is.EqualTo("last progress note"));
    }

    [Test]
    public void Complete_WithMessage_OverwritesMessage()
    {
        var mgr = new JobManager();
        var job = mgr.Create(JobKind.Execution, "$graph");

        job.Complete("{\"success\":true}");

        var snap = mgr.Get(job.JobId)!.Value;
        Assert.That(snap.State, Is.EqualTo(JobState.Completed));
        Assert.That(snap.Message, Is.EqualTo("{\"success\":true}"));
    }

    [Test]
    public void Fail_SetsFailedStateWithMessage()
    {
        var mgr = new JobManager();
        var job = mgr.Create(JobKind.Persistent, "n1");

        job.Fail("boom");

        var snap = mgr.Get(job.JobId)!.Value;
        Assert.That(snap.State, Is.EqualTo(JobState.Failed));
        Assert.That(snap.Message, Is.EqualTo("boom"));
    }

    [Test]
    public void Get_UnknownJobId_ReturnsNull()
    {
        var mgr = new JobManager();
        Assert.That(mgr.Get("does-not-exist"), Is.Null);
    }

    [Test]
    public void GetJobsForNodes_FiltersByNodeIdAndCreationTime()
    {
        var mgr = new JobManager();
        var before = DateTime.UtcNow;
        var jobA = mgr.Create(JobKind.Persistent, "nodeA");
        var jobB = mgr.Create(JobKind.Persistent, "nodeB");
        mgr.Create(JobKind.Persistent, "nodeC"); // 対象外ノード

        var result = mgr.GetJobsForNodes(new[] { "nodeA", "nodeB" }, before);

        Assert.That(result.Select(j => j.JobId), Is.EquivalentTo(new[] { jobA.JobId, jobB.JobId }));
    }

    [Test]
    public void GetJobsForNodes_ExcludesJobsCreatedBeforeSince()
    {
        var mgr = new JobManager();
        var oldJob = mgr.Create(JobKind.Persistent, "n1");
        var cutoff = DateTime.UtcNow.AddMilliseconds(50);
        Thread.Sleep(60);
        var newJob = mgr.Create(JobKind.Persistent, "n1");

        var result = mgr.GetJobsForNodes(new[] { "n1" }, cutoff);

        Assert.That(result.Select(j => j.JobId), Is.EquivalentTo(new[] { newJob.JobId }));
        Assert.That(result.Select(j => j.JobId), Does.Not.Contain(oldJob.JobId));
    }

    [Test]
    public void PruneExpired_DoesNotRemoveFreshJobs()
    {
        var mgr = new JobManager();
        var job = mgr.Create(JobKind.Persistent, "n1");
        job.Complete();

        mgr.PruneExpired();

        Assert.That(mgr.Get(job.JobId), Is.Not.Null, "TTL未経過のJobは間引かれない");
    }

    [Test]
    public void ConcurrentReportProgress_DoesNotThrowOrCorruptState()
    {
        var mgr = new JobManager();
        var job = mgr.Create(JobKind.Persistent, "n1");
        job.SetRunning();

        Parallel.For(0, 200, i => job.ReportProgress($"progress {i}"));

        var snap = mgr.Get(job.JobId)!.Value;
        Assert.That(snap.State, Is.EqualTo(JobState.Running));
        Assert.That(snap.Message, Does.StartWith("progress "));
    }
}
