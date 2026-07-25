namespace NodeGraphModLab.Server;

/// <summary>
/// スクリプトホットリロードの一時停止状態を NgolRuntime と WS ハンドラ間で共有するゲート。
/// 一時停止中は NgolRuntime 側のデバウンスドレイン処理がスキップされ、再開時に自然と再開する。
/// GraphServer の公開コンストラクタの引数型になるため public にしている。
/// </summary>
public sealed class HotReloadGate
{
    private volatile bool _paused;
    private Func<int>? _pendingCountProvider;

    public bool IsPaused => _paused;

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    /// <summary>NgolRuntime が保留中ファイル数を返すデリゲートを初期化時に1回だけ登録する。</summary>
    internal void SetPendingCountProvider(Func<int> provider) => _pendingCountProvider = provider;

    public int GetPendingCount() => _pendingCountProvider?.Invoke() ?? 0;
}
