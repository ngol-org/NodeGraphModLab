namespace NodeGraphModLab;

public sealed class NgolRuntimeOptions
{
    /// <summary>
    /// true のとき MonoBehaviour を使わず Timer ループで Update を代替する（IL2CPP interop 不可時のフォールバック）。
    /// </summary>
    public bool EnableDirectMode { get; init; } = false;

    /// <summary>
    /// GC.Collect ワークアラウンドを有効にする（Unity 6 未満 + IL2CPP でのみ必要）。
    /// 呼び出し側が Unity バージョンを検出して設定すること。
    /// </summary>
    public bool EnableGcWorkaround { get; init; } = false;

    /// <summary>
    /// Direct モード時、専用ドレインスレッドの開始直後に一度だけ呼ばれるコールバック。
    /// ゲーム固有の IL2CPP スレッドアタッチ処理などを渡す。
    /// </summary>
    public Action? DirectModeDrainSetup { get; init; }

    /// <summary>WebSocket 接続時に welcome メッセージで返すプラグインバージョン文字列。</summary>
    public string PluginVersion { get; init; } = string.Empty;

    /// <summary>WebSocket 接続時に welcome メッセージで返す接続先ゲームプロセス名。</summary>
    public string GameName { get; init; } = string.Empty;

    /// <summary>
    /// WebSocket 接続時に welcome メッセージで返す実行環境種別文字列（例: "IL2CPP" / "Mono"）。
    /// 未指定（null）の場合、GraphServer は <see cref="System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription"/>
    /// を既定値として使う。IL2CPP/Mono の区別が必要なホストはここで明示的に指定すること。
    /// </summary>
    public string? RuntimeType { get; init; }
}
