namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// RegisterPersistent に渡すコールバックのセット。
/// ホスト固有の更新フェーズ（Unityの LateUpdate/FixedUpdate/OnGUI 等）を追加したい場合は、
/// このクラスを継承し GetPhase(string) をオーバーライドして拡張する（ホストブリッジ側の責務）。
/// IExecutionContext のシグネチャは変更しなくてよい。
/// </summary>
public class PersistentCallbacks
{
    public Action? OnUpdate { get; init; }

    /// <summary>
    /// ホスト固有の拡張フェーズ名からコールバックを解決する。既定は常に null（未対応）。
    /// サブクラスでオーバーライドし、フェーズ名（例: "Unity.OnGUI"）を自身のプロパティへマッピングする。
    /// </summary>
    public virtual Action? GetPhase(string phaseName) => null;

    // ---- カテゴリ B: NGOL Registration ライフサイクルイベント ----
    // PersistentNodeRunner が IPersistentRegistration の生死を管理する過程で呼ぶ。
    // ホスト側の常駐コンポーネントが生存中、何度でも Start/Stop サイクルが起きうる。

    /// <summary>
    /// 登録後、最初の Drain*() 呼び出しで 1 回だけ対象コールバックより前に呼ばれる。
    /// 呼び出しは必ずホストのメインスレッドから行われる。
    /// Execute() は背景スレッドから呼ばれる場合があるため、メインスレッド専用の初期化処理はここに置く。
    /// </summary>
    public Action? OnStart { get; init; }

    /// <summary>
    /// 登録が停止されるタイミングで呼ばれる。
    /// 呼び出しは必ずホストのメインスレッドから行われる（DrainUpdate または ClearAll 経由）。
    /// WebSocket・スレッド・ホスト側オブジェクト等のリソース解放に使用する。
    /// Cancel() 呼び出し後、次の DrainUpdate() で発火する（二重呼び出し防止済み）。
    /// </summary>
    public Action? OnStop { get; init; }
}
