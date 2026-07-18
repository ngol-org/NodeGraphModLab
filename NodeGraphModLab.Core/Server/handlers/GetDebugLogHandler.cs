using System.Linq;
using System.Text.Json;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server.Handlers;

internal sealed class GetDebugLogHandler : IMessageHandler
{
    private const int MaxMessageLength = 2048;

    private readonly HandlerContext _ctx;
    public string MessageType => "get_debug_log";

    public GetDebugLogHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var count = root.TryGetProperty("count", out var c) ? c.GetInt32() : 10;
        var filter = new DebugLogFilter
        {
            Kind = root.TryGetProperty("kind", out var k) ? k.GetString() : null,
            Level = root.TryGetProperty("level", out var l) ? l.GetString() : null,
            MessageContains = root.TryGetProperty("messageContains", out var mc) ? mc.GetString() : null,
            DomEventType = root.TryGetProperty("domEventType", out var det) ? det.GetString() : null,
            SinceMs = root.TryGetProperty("sinceMs", out var sm) ? sm.GetInt64() : null,
            UntilMs = root.TryGetProperty("untilMs", out var um) ? um.GetInt64() : null,
            LevelAtLeast = root.TryGetProperty("levelAtLeast", out var la) ? la.GetString() : null,
            MessageRegex = root.TryGetProperty("messageRegex", out var mr) ? mr.GetString() : null
        };
        var entries = _ctx.DebugLogStore.GetFiltered(filter, count);
        var dto = new GetDebugLogResponse
        {
            Entries = entries.Select(e => new DebugLogEntryDto
            {
                Kind = e.Kind,
                Level = e.Level,
                Message = Truncate(e.Message),
                TimestampMs = new DateTimeOffset(e.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds()
            }).ToList()
        };
        await session.SendAsync(JsonSerializer.Serialize(dto, ServerJsonContext.Default.GetDebugLogResponse));
    }

    private static string Truncate(string message) =>
        message.Length > MaxMessageLength
            ? message.Substring(0, MaxMessageLength) + "...[truncated]"
            : message;
}
