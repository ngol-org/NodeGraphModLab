using System.Collections.Concurrent;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

/// <summary>
/// ブラウザへ投げたキャンバス取得要求の待ち合わせ。
///
/// 要求元と応答元が別のセッションになるため、応答を要求へ突き合わせる手掛かりが要る。
/// 既存の応答は種別だけで照合しているが（サーバーは requestId を読んでいない）、
/// 同じ種別の応答が複数タブから同時に飛びうるこの経路では種別だけでは足りない。
/// </summary>
internal sealed class PendingCanvasRequests
{
    private sealed class Entry
    {
        public TaskCompletionSource<NodeGraph?> Completion { get; }
        public string SessionId { get; }

        public Entry(string sessionId)
        {
            SessionId = sessionId;
            // 既定では応答側スレッド上で継続が同期実行される。ここでの応答側は
            // ブラウザセッションの受信ループなので、要求元の後処理でそのループを塞いでしまう。
            Completion = new TaskCompletionSource<NodeGraph?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public Task<NodeGraph?> Register(string token, string sessionId)
    {
        var entry = new Entry(sessionId);
        _entries[token] = entry;
        return entry.Completion.Task;
    }

    /// <summary>応答が届いたら待ち合わせを解く。知らない token は無視する（別要求の残り）。</summary>
    public bool Complete(string token, NodeGraph? graph)
    {
        if (!_entries.TryRemove(token, out var entry)) return false;
        return entry.Completion.TrySetResult(graph);
    }

    /// <summary>タイムアウト・後始末で捨てる。待っている側には null が渡る。</summary>
    public void Abandon(string token)
    {
        if (_entries.TryRemove(token, out var entry)) entry.Completion.TrySetResult(null);
    }

    /// <summary>
    /// 切断したタブ宛の要求を捨てる。応答は永久に来ないため、待ち続ける意味がない。
    /// </summary>
    public void AbandonSession(string sessionId)
    {
        foreach (var pair in _entries)
        {
            if (pair.Value.SessionId == sessionId) Abandon(pair.Key);
        }
    }

    public int Count => _entries.Count;
}
