#if !NET6_0_OR_GREATER
using System;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeGraphModLab.Server;

/// <summary>
/// Mono の HttpListener は WebSocket (AcceptWebSocketAsync) をサポートしないため、
/// TcpListener ベースの独自 WebSocket 実装。RFC 6455 の基本フレーム処理のみ実装。
/// </summary>
internal sealed class TcpWebSocket : WebSocket
{
    private readonly Stream _stream;
    private readonly Socket _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private WebSocketState _state = WebSocketState.Open;

    public TcpWebSocket(Stream stream, Socket socket)
    {
        _stream = stream;
        _socket = socket;
        _socket.Blocking = true;
    }

    // ---- WebSocket 抽象メンバーの実装 ----
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;

    public override void Abort()
    {
        _state = WebSocketState.Aborted;
        try { _stream.Dispose(); } catch { }
    }

    public override void Dispose()
    {
        if (_state != WebSocketState.Closed && _state != WebSocketState.Aborted)
            _state = WebSocketState.Closed;
        try { _stream.Dispose(); } catch { }
    }

    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
        bool endOfMessage, CancellationToken cancellationToken)
    {
        if (_state != WebSocketState.Open) return;
        byte opcode = messageType == WebSocketMessageType.Text ? (byte)0x01 : (byte)0x02;
        var frame = BuildServerFrame(opcode, buffer.Array!, buffer.Offset, buffer.Count);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() =>
            {
                int sent = 0;
                while (sent < frame.Length)
                    sent += _socket.Send(frame, sent, frame.Length - sent, SocketFlags.None);
            }, cancellationToken);
        }
        finally { _sendLock.Release(); }
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        var (msgType, payload, isClose) = await ReadFrameAsync(cancellationToken);
        if (isClose)
        {
            _state = WebSocketState.CloseReceived;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true,
                WebSocketCloseStatus.NormalClosure, string.Empty);
        }
        int count = Math.Min(payload.Length, buffer.Count);
        Buffer.BlockCopy(payload, 0, buffer.Array!, buffer.Offset, count);
        return new WebSocketReceiveResult(count, msgType, true);
    }

    public override async Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken)
    {
        if (_state == WebSocketState.Open || _state == WebSocketState.CloseReceived)
        {
            ushort code = (ushort)closeStatus;
            var codeBytes = new byte[] { (byte)(code >> 8), (byte)(code & 0xFF) };
            var frame = BuildServerFrame(0x08, codeBytes, 0, codeBytes.Length);
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await Task.Run(() =>
                {
                    int sent = 0;
                    while (sent < frame.Length)
                        sent += _socket.Send(frame, sent, frame.Length - sent, SocketFlags.None);
                }, cancellationToken);
            }
            finally { _sendLock.Release(); }
        }
        _state = WebSocketState.Closed;
    }

    public override async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken)
    {
        await CloseAsync(closeStatus, statusDescription, cancellationToken);
    }

    // ---- RFC 6455 フレームの読み書き ----

    private async Task<(WebSocketMessageType, byte[], bool)> ReadFrameAsync(CancellationToken ct)
    {
        byte[] header = new byte[2];
        await ReadExactAsync(header, 2, ct);

        byte opcode = (byte)(header[0] & 0x0F);
        bool masked = (header[1] & 0x80) != 0;
        long payloadLen = header[1] & 0x7F;

        if (payloadLen == 126)
        {
            byte[] ext = new byte[2];
            await ReadExactAsync(ext, 2, ct);
            payloadLen = (ext[0] << 8) | ext[1];
        }
        else if (payloadLen == 127)
        {
            byte[] ext = new byte[8];
            await ReadExactAsync(ext, 8, ct);
            payloadLen = 0;
            for (int i = 0; i < 8; i++) payloadLen = (payloadLen << 8) | ext[i];
        }

        byte[]? mask = null;
        if (masked)
        {
            mask = new byte[4];
            await ReadExactAsync(mask, 4, ct);
        }

        byte[] payload = new byte[payloadLen];
        await ReadExactAsync(payload, (int)payloadLen, ct);

        if (masked && mask != null)
            for (int i = 0; i < payload.Length; i++)
                payload[i] ^= mask[i % 4];

        bool isClose = opcode == 0x08;
        var msgType = opcode == 0x01 ? WebSocketMessageType.Text : WebSocketMessageType.Binary;
        return (msgType, payload, isClose);
    }

    private async Task ReadExactAsync(byte[] buffer, int count, CancellationToken ct)
    {
        int offset = 0;
        while (offset < count)
        {
            bool ready = await Task.Run(
                () => _socket.Poll(5_000_000, SelectMode.SelectRead), ct);
            if (!ready) { ct.ThrowIfCancellationRequested(); continue; }
            int n = _socket.Receive(buffer, offset, count - offset, SocketFlags.None);
            if (n == 0) throw new IOException("WebSocket connection closed unexpectedly");
            offset += n;
        }
    }

    /// <summary>サーバー → クライアント方向はマスクなしで送信する（RFC 6455 §5.3）。</summary>
    private static byte[] BuildServerFrame(byte opcode, byte[] data, int offset, int count)
    {
        // FIN=1, opcode, no mask
        using var ms = new MemoryStream();
        ms.WriteByte((byte)(0x80 | (opcode & 0x0F)));
        if (count <= 125)
        {
            ms.WriteByte((byte)count);
        }
        else if (count <= 65535)
        {
            ms.WriteByte(126);
            ms.WriteByte((byte)(count >> 8));
            ms.WriteByte((byte)(count & 0xFF));
        }
        else
        {
            ms.WriteByte(127);
            long countLong = (long)count;
            for (int i = 7; i >= 0; i--)
                ms.WriteByte((byte)((countLong >> (i * 8)) & 0xFF));
        }
        ms.Write(data, offset, count);
        return ms.ToArray();
    }
}

/// <summary>
/// Mono 用 HTTP リクエスト解析結果。
/// </summary>
internal sealed class RawHttpRequest
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public System.Collections.Generic.Dictionary<string, string> Headers { get; } = new();

    public bool IsWebSocketUpgrade =>
        Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
        && Headers.TryGetValue("upgrade", out var u)
            && u.Trim().Equals("websocket", StringComparison.OrdinalIgnoreCase)
        && Headers.TryGetValue("connection", out var c)
            && c.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) >= 0;

    public string? WebSocketKey =>
        Headers.TryGetValue("sec-websocket-key", out var k) ? k.Trim() : null;
}

/// <summary>
/// TCP ストリームから HTTP リクエストヘッダーを読み取るヘルパー。
/// </summary>
internal static class RawHttpParser
{
    public static async Task<RawHttpRequest?> ParseAsync(Stream stream, CancellationToken ct = default)
    {
        var lines = new System.Collections.Generic.List<string>();
        var sb = new StringBuilder();
        byte[] buf = new byte[1];
        char prev = '\0';

        while (true)
        {
            int n = await stream.ReadAsync(buf, 0, 1, ct);
            if (n == 0) return null;
            char ch = (char)buf[0];
            if (prev == '\r' && ch == '\n')
            {
                var line = sb.ToString().TrimEnd('\r');
                sb.Clear();
                if (line.Length == 0) break; // blank line = end of headers
                lines.Add(line);
            }
            else
            {
                sb.Append(ch);
            }
            prev = ch;
        }

        if (lines.Count == 0) return null;

        // Request line: GET /path HTTP/1.1
        var parts = lines[0].Split(new char[] { ' ' }, 3);
        var req = new RawHttpRequest
        {
            Method = parts.Length > 0 ? parts[0] : "GET",
            Path = parts.Length > 1 ? parts[1] : "/"
        };

        for (int i = 1; i < lines.Count; i++)
        {
            int sep = lines[i].IndexOf(':');
            if (sep > 0)
            {
                var key = lines[i].Substring(0, sep).Trim().ToLowerInvariant();
                var val = lines[i].Substring(sep + 1).Trim();
                req.Headers[key] = val;
            }
        }

        return req;
    }
}

/// <summary>
/// TCP ストリーム上で RFC 6455 WebSocket ハンドシェイクを行うヘルパー。
/// </summary>
internal static class MonoWebSocketHelper
{
    private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public static async Task<TcpWebSocket?> AcceptAsync(System.Net.Sockets.TcpClient client, RawHttpRequest req, string? subProtocol)
    {
        var key = req.WebSocketKey;
        if (key == null) return null;

        // Sec-WebSocket-Accept を計算（RFC 6455 §4.2.2）
        string combined = key + WsGuid;
        byte[] hash;
        using (var sha1 = SHA1.Create())
            hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(combined));
        var accept = Convert.ToBase64String(hash);

        var response = new StringBuilder();
        response.Append("HTTP/1.1 101 Switching Protocols\r\n");
        response.Append("Upgrade: websocket\r\n");
        response.Append("Connection: Upgrade\r\n");
        response.Append($"Sec-WebSocket-Accept: {accept}\r\n");
        if (subProtocol != null)
            response.Append($"Sec-WebSocket-Protocol: {subProtocol}\r\n");
        response.Append("\r\n");

        var stream = client.GetStream();
        var bytes = Encoding.ASCII.GetBytes(response.ToString());
        await stream.WriteAsync(bytes, 0, bytes.Length);

        return new TcpWebSocket(stream, client.Client);
    }
}
#endif
