namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// ノードクイック起動情報。
/// <see cref="IExecutionContext.GetNodesByInputPortType"/> で返される。
/// </summary>
public readonly struct NodeQuickLaunchInfo
{
    public string TypeId         { get; init; }
    public string DisplayName    { get; init; }
    public string Category       { get; init; }
    /// <summary>マッチした最初の入力ポート名（QuickExecuteNode の inputPortName に使用）。</summary>
    public string InputPortName  { get; init; }
}

/// <summary>
/// 出力ポートから接続された下流入力ポートを表す。
/// <see cref="IExecutionContext.GetDownstreamConnections"/> で使用する。
/// </summary>
public readonly struct PortConnection
{
    public string NodeInstanceId { get; }
    public string PortName { get; }

    public PortConnection(string nodeInstanceId, string portName)
    {
        NodeInstanceId = nodeInstanceId;
        PortName = portName;
    }
}

/// <summary>
/// ノード実行コンテキスト。
/// ホストの内部状態へのアクセス、ポート値の受け渡し、ログ出力を提供する。
/// </summary>
public interface IExecutionContext
{
    /// <summary>ホストのログへの出力インターフェース。</summary>
    INodeLogger Logger { get; }

    /// <summary>
    /// ホストのメインスレッドでアクションを実行する。
    /// スレッドセーフでないホスト内部状態へのアクセスはすべてここ経由で行うこと。
    /// </summary>
    void MainThreadDispatch(Action action);

    /// <summary>入力ポートの値を取得する。</summary>
    /// <param name="portName">ポート名</param>
    /// <returns>値 (null の場合、未接続または入力なし)</returns>
    object? GetPortValue(string portName);

    /// <summary>出力ポートに値をセットする。</summary>
    /// <param name="portName">ポート名</param>
    /// <param name="value">出力値</param>
    void SetPortValue(string portName, object? value);

    /// <summary>現在のノードインスタンス ID。</summary>
    string NodeInstanceId { get; }

    /// <summary>ノードのパラメータ値を取得する（エディタで設定した固定値）。</summary>
    T? GetParam<T>(string paramName);

    /// <summary>
    /// WebUI の push_node_live_params でマージされたライブパラメータを取得する。
    /// 永続ノードの OnUpdate（または GetPhase 経由のホスト拡張コールバック）内で opt-in して読む。未設定時は defaultValue を返す。
    /// </summary>
    T GetLiveParam<T>(string key, T defaultValue = default!);

    /// <summary>
    /// 毎フレーム呼ばれるコールバックを登録する。
    /// 使用するフェーズのみ PersistentCallbacks に設定すればよい。
    /// 返された IPersistentRegistration.Cancel() で登録を解除できる。
    /// </summary>
    IPersistentRegistration RegisterPersistent(PersistentCallbacks callbacks);

    /// <summary>
    /// 断片グラフ連携用スナップショットストア。
    /// SnapshotNode はここへ値を保存し、次断片実行時に FragmentLink 経由で注入される。
    /// 断片実行以外のコンテキストでは null の場合がある。
    /// </summary>
    ISnapshotStore? SnapshotStore { get; }

    /// <summary>
    /// この実行ノードの指定出力ポートに接続された下流入力ポートの一覧を返す。
    /// 永続コールバックからダウンストリームに値をプッシュする際に使用する。
    /// </summary>
    IReadOnlyList<PortConnection> GetDownstreamConnections(string outputPortName);

    /// <summary>
    /// グラフ実行外（RegisterPersistent コールバック等）から接続先 SnapshotStore に値を書き込み、
    /// WebUI へリアルタイム通知する。
    /// 出力ポートに接続されたすべての下流ノードの入力スロットを更新する。
    /// </summary>
    void PushLiveValue(string portName, object? value);

    /// <summary>
    /// 指定の入力ポートタイプ（DataType 名）を持つノード一覧を返す。
    /// クイック起動メニューやノード連携 UI から使用する。
    /// 実装がノードレジストリを持たない場合は空リストを返す。
    /// </summary>
    IReadOnlyList<NodeQuickLaunchInfo> GetNodesByInputPortType(string portType);

    /// <summary>
    /// 指定ノードタイプを指定入力で単独実行する（クイック起動）。
    /// 必ずメインスレッドから呼ぶこと。
    /// 実装がノードレジストリを持たない場合は no-op。
    /// </summary>
    void QuickExecuteNode(string nodeTypeId, string inputPortName, object? inputValue);

    /// <summary>
    /// ノード間共有キーバリューストア。
    /// ゲームセッションをまたいで値を永続保持する。
    /// </summary>
    IKVStore Store { get; }

    /// <summary>
    /// 後付け Extension が登録した共有サービスを取得する。未登録時は null。
    /// </summary>
    T? GetExtensionService<T>() where T : class;
}

/// <summary>
/// ノード内から使用するロガーインターフェース。
/// </summary>
public interface INodeLogger
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogDebug(string message);
}
