namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// 断片グラフ連携用スナップショットストア。
/// SnapshotNode が値を保存し、FragmentLink 経由で次断片の入力へ注入される。
/// 実装はセッション中インメモリで保持する。
/// </summary>
public interface ISnapshotStore
{
    /// <summary>スナップショット値を保存する。</summary>
    void SetSnapshot(string nodeInstanceId, string portName, object? value);

    /// <summary>保存済みスナップショット値を取得する。存在しない場合は null。</summary>
    object? GetSnapshot(string nodeInstanceId, string portName);

    /// <summary>指定ノード・ポートのスナップショットが存在するかどうか。</summary>
    bool HasSnapshot(string nodeInstanceId, string portName);

    /// <summary>全スナップショットをクリアする。</summary>
    void Clear();

    /// <summary>
    /// ライブ値をストアに書き込む。通知ロジックは実装クラスに委譲する。
    /// </summary>
    void PushLive(string nodeInstanceId, string portName, object? value);

    /// <summary>
    /// 下流 Snapshot ノードのスロットへライブ値を書き込む。
    /// </summary>
    void PushLiveToSnapshot(string nodeInstanceId, string portName, object? value);
}
