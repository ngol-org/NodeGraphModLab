namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// ホストまたはNGOLコアの更新サイクルごとに呼ばれるコールバックの登録・管理を表します。
/// Cancel() で登録を解除でき、IsActive で状態を確認できます。
/// </summary>
public interface IPersistentRegistration : IDisposable
{
    /// <summary>登録がアクティブかどうか。Cancel() で false になります。</summary>
    bool IsActive { get; }

    /// <summary>登録を解除します。</summary>
    void Cancel();
}
