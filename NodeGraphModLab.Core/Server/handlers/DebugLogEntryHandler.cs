using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class DebugLogEntryHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "debug_log_entry";

    public DebugLogEntryHandler(HandlerContext ctx) { _ctx = ctx; }

    public Task HandleAsync(ISession session, JsonElement root)
    {
        var kind = root.TryGetProperty("kind", out var k) ? k.GetString() : "console";
        var level = root.TryGetProperty("level", out var l) ? l.GetString() : "log";
        var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
        if (string.IsNullOrEmpty(message)) return Task.CompletedTask;
        _ctx.DebugLogStore.Add(kind ?? "console", level ?? "log", message);
        return Task.CompletedTask;
    }
}
