using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Server;
using NodeGraphModLab.Server.Handlers;

namespace NodeGraphModLab.Tests.Handlers;

[TestFixture]
public class SaveLoadGraphHandlerTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ngol_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private HandlerContext MakeCtx() => new(
        new NodeRegistry(), new LiveParamStore(), new DebugLogStore(), log: null,
        graphSaveDir: _tempDir, dynamicNodesDir: _tempDir,
        nodesDir: _tempDir, nodePacksDir: _tempDir,
        scriptNodeId: new ConcurrentDictionary<string, string>(),
        pendingExecutions: new ConcurrentQueue<PendingExecution>(),
        runner: new PersistentNodeRunner(),
        getExecutionCts: () => null,
        executor: new GraphExecutor(new NodeRegistry()));

    [Test]
    public async Task SaveHandler_ValidGraph_WriteFileAndReturnsSuccess()
    {
        var ctx = MakeCtx();
        var handler = new SaveGraphHandler(ctx);
        var session = new TestSession();
        var graph = new NodeGraph { Id = "graph-001", Name = "Test Graph" };
        using var doc = JsonDocument.Parse($"{{\"graph\":{graph.ToJson()}}}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempDir, "graph-001.json")), Is.True);
    }

    [Test]
    public async Task LoadHandler_ExistingId_ReturnsGraph()
    {
        var ctx = MakeCtx();
        var graph = new NodeGraph { Id = "graph-002", Name = "Saved Graph" };
        File.WriteAllText(Path.Combine(_tempDir, "graph-002.json"), graph.ToJson());

        var handler = new LoadGraphHandler(ctx);
        var session = new TestSession();
        using var doc = JsonDocument.Parse("{\"id\":\"graph-002\"}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(resp.RootElement.GetProperty("graph").ValueKind, Is.Not.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task LoadHandler_MissingId_ReturnsFailure()
    {
        var ctx = MakeCtx();
        var handler = new LoadGraphHandler(ctx);
        var session = new TestSession();
        using var doc = JsonDocument.Parse("{\"id\":\"nonexistent\"}");

        await handler.HandleAsync(session, doc.RootElement);

        Assert.That(session.SentMessages, Has.Count.EqualTo(1));
        using var resp = JsonDocument.Parse(session.SentMessages[0]);
        Assert.That(resp.RootElement.GetProperty("success").GetBoolean(), Is.False);
    }
}
