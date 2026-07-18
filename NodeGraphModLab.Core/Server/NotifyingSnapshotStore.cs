using System.Collections.Concurrent;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

/// <summary>
/// InMemorySnapshotStore にWebSocket通知責務を追加したラッパー。
/// LiveNotifications キュー・OnSet コールバック・CanSet ガードを担当する。
/// plugin/Server 層の関心事のみを持ち、NodeAPI アセンブリには含まれない。
/// </summary>
public sealed class NotifyingSnapshotStore : InMemorySnapshotStore
{
    /// <summary>スナップショット保存時に呼ばれるコールバック。(nodeInstanceId, portName, value) を受け取る。</summary>
    public Action<string, string, object?>? OnSet { get; set; }

    /// <summary>保存可否を判定するコールバック。null の場合は常に許可。false を返すとスキップ。</summary>
    public Func<string, bool>? CanSet { get; set; }

    /// <summary>
    /// PushLive から通知されたライブ値変更の未送信キュー。
    /// GraphServer がホストの更新サイクルごとにデキューして WebSocket ブロードキャストする。
    /// </summary>
    public ConcurrentQueue<(string nodeId, string portName, object? value)> LiveNotifications { get; }
        = new ConcurrentQueue<(string, string, object?)>();

    private bool IsAllowed(string nodeInstanceId) => CanSet == null || CanSet(nodeInstanceId);

    public override void SetSnapshot(string nodeInstanceId, string portName, object? value)
    {
        if (!IsAllowed(nodeInstanceId)) return;
        base.SetSnapshot(nodeInstanceId, portName, value);
        OnSet?.Invoke(nodeInstanceId, portName, value);
    }

    public override void PushLive(string nodeInstanceId, string portName, object? value)
    {
        if (!IsAllowed(nodeInstanceId)) return;
        base.PushLive(nodeInstanceId, portName, value);
        LiveNotifications.Enqueue((nodeInstanceId, portName, value));
    }

    public override void PushLiveToSnapshot(string nodeInstanceId, string portName, object? value)
    {
        if (!IsAllowed(nodeInstanceId)) return;
        base.PushLiveToSnapshot(nodeInstanceId, portName, value);
        LiveNotifications.Enqueue((nodeInstanceId, portName, value));
    }
}
