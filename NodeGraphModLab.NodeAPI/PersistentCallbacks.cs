namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// RegisterPersistent に渡すコールバックのセット。
/// ホスト固有の更新フェーズ（描画後・物理更新後など）を追加したい場合は、
/// このクラスを継承し GetPhase(string) をオーバーライドして拡張する（ホストブリッジ側の責務）。
/// IExecutionContext のシグネチャは変更しなくてよい。
///
/// 本 API で「ホストのメインスレッド」と呼ぶのは NGOL を駆動するスレッド、すなわち
/// ホストが Tick() を呼ぶスレッド、または Direct モードの専用ドレインスレッドを指す。
/// プロセスの起動スレッドと一致するとは限らない。
/// </summary>
public class PersistentCallbacks
{
    public Action? OnUpdate { get; init; }

    /// <summary>
    /// ホスト固有の拡張フェーズ名からコールバックを解決する。既定は常に null（未対応）。
    /// サブクラスでオーバーライドし、フェーズ名（例: "Unity.OnGUI"）を自身のプロパティへマッピングする。
    /// フェーズを排出するのはホストブリッジなので、Direct モードでは発火しない。
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
    /// WebSocket・スレッド・ホスト側オブジェクト等のリソース解放に使用する。
    /// 停止要求の後、次の DrainUpdate() でホストのメインスレッドから発火する（二重呼び出し防止済み）。
    /// ただしホスト終了時のみ、DrainUpdate を待たず Dispose() の呼び出し元スレッドから同期発火する。
    /// </summary>
    public Action? OnStop { get; init; }
}
