using System.Collections.Generic;
using System.Threading.Tasks;
using NodeGraphModLab.Server;

namespace NodeGraphModLab.Tests.Handlers;

/// <summary>
/// ハンドラ単体テスト用の ISession スタブ。
/// SendAsync の呼び出しをキャプチャして検証に使用する。
/// </summary>
public sealed class TestSession : ISession
{
    public List<string> SentMessages { get; } = new();
    public NotifyingSnapshotStore SnapshotStore { get; } = new();
    public HashSet<string> PinnedSnapshotNodeIds { get; } = new();

    public Task SendAsync(string message)
    {
        SentMessages.Add(message);
        return Task.CompletedTask;
    }
}
