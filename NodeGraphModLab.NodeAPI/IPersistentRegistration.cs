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

    /// <summary>
    /// この永続ノードに紐づくJobの状況メッセージ（自由記述）を更新します。完全にオプトインで、
    /// 呼ばなければ check_job_status の結果は null のままです（既存ノードは無改修で動作します）。
    /// </summary>
    void ReportProgress(string message);
}
