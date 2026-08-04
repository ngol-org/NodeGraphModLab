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

    // 呼び出し側のバッファに入りきらなかった分を次の ReceiveAsync へ持ち越す。
    private byte[]? _carry;
    private int _carryOffset;
    private bool _carryFinal;
    private WebSocketMessageType _carryType;

    // 断片化されたメッセージの先頭フレームの種別。継続フレーム（opcode 0x00）は
    // 自身では種別を持たないため、先頭フレームの種別を引き継ぐ（RFC 6455 §5.4）。
    private WebSocketMessageType _fragmentType = WebSocketMessageType.Text;
    private bool _inFragmentedMessage;

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
        // 前回の呼び出しで渡しきれなかった分が残っていれば、それを先に返す。
        if (_carry != null)
            return TakeFromCarry(buffer);

        var (msgType, payload, isClose, isFinal) = await ReadFrameAsync(cancellationToken);
        if (isClose)
        {
            _state = WebSocketState.CloseReceived;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true,
                WebSocketCloseStatus.NormalClosure, string.Empty);
        }

        _carry = payload;
        _carryOffset = 0;
        _carryFinal = isFinal;
        _carryType = msgType;
        return TakeFromCarry(buffer);
    }

    /// <summary>
    /// 保留中のペイロードから呼び出し側のバッファに入る分だけ写す。
    /// 残りがある間は EndOfMessage=false を返し、呼び出し側に続きを取りに来させる
    /// （従来は Math.Min で切って超過分を黙って捨てていた）。
    /// </summary>
    private WebSocketReceiveResult TakeFromCarry(ArraySegment<byte> buffer)
    {
        var payload = _carry!;
        int remaining = payload.Length - _carryOffset;
        int count = Math.Min(remaining, buffer.Count);
        Buffer.BlockCopy(payload, _carryOffset, buffer.Array!, buffer.Offset, count);
        _carryOffset += count;

        bool drained = _carryOffset >= payload.Length;
        var type = _carryType;
        bool endOfMessage = drained && _carryFinal;
        if (drained)
        {
            _carry = null;
            _carryOffset = 0;
        }
        return new WebSocketReceiveResult(count, type, endOfMessage);
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

    /// <summary>
    /// データフレームを1つ読む。制御フレーム（Ping/Pong）はデータの途中にも割り込めるため
    /// （RFC 6455 §5.4）、ここで消化して次のフレームまで読み進める。
    /// </summary>
    private async Task<(WebSocketMessageType Type, byte[] Payload, bool IsClose, bool IsFinal)> ReadFrameAsync(CancellationToken ct)
    {
        while (true)
        {
            var (opcode, payload, isFinal) = await ReadRawFrameAsync(ct);

            switch (opcode)
            {
                case 0x08: // Close
                    return (WebSocketMessageType.Close, Array.Empty<byte>(), true, true);

                case 0x09: // Ping — 同じペイロードを Pong で返す
                    // 制御フレームのペイロードは 125 バイト以下と定められている（RFC 6455 §5.5）。
                    // 規格違反の長い Ping をそのまま返して自分が違反しないよう切り詰める。
                    if (payload.Length > 125) Array.Resize(ref payload, 125);
                    await SendControlFrameAsync(0x0A, payload, ct);
                    continue;

                case 0x0A: // Pong — 応答は不要
                    continue;

                case 0x00: // 継続フレーム。種別は先頭フレームから引き継ぐ
                    if (!_inFragmentedMessage)
                        throw new IOException("WebSocket: 断片化が始まっていないのに継続フレームを受信しました");
                    if (isFinal) _inFragmentedMessage = false;
                    return (_fragmentType, payload, false, isFinal);

                case 0x01: // Text
                case 0x02: // Binary
                    var type = opcode == 0x01 ? WebSocketMessageType.Text : WebSocketMessageType.Binary;
                    if (!isFinal)
                    {
                        _fragmentType = type;
                        _inFragmentedMessage = true;
                    }
                    return (type, payload, false, isFinal);

                default:
                    throw new IOException($"WebSocket: 未知の opcode 0x{opcode:X2} を受信しました");
            }
        }
    }

    /// <summary>1フレームをそのまま読み取る（opcode・ペイロード・FIN ビット）。</summary>
    private async Task<(byte Opcode, byte[] Payload, bool IsFinal)> ReadRawFrameAsync(CancellationToken ct)
    {
        byte[] header = new byte[2];
        await ReadExactAsync(header, 2, ct);

        bool isFinal = (header[0] & 0x80) != 0;
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

        return (opcode, payload, isFinal);
    }

    private async Task SendControlFrameAsync(byte opcode, byte[] payload, CancellationToken ct)
    {
        if (_state != WebSocketState.Open) return;
        var frame = BuildServerFrame(opcode, payload, 0, payload.Length);
        await _sendLock.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                int sent = 0;
                while (sent < frame.Length)
                    sent += _socket.Send(frame, sent, frame.Length - sent, SocketFlags.None);
            }, ct);
        }
        finally { _sendLock.Release(); }
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
