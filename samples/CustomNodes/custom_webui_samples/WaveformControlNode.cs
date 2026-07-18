/// <summary>
/// WebUI ウィジェット（周波数・振幅スライダー）で調整した値を、接続先ノードへ
/// リアルタイムにストリーミングするサンプルノード。単体では何もしないが、
/// LiveWaveformNode（sample.webui_live_waveform）の frequency/amplitude 入力に
/// 接続すると、スライダー操作が即座に波形へ反映される「複数ノードの拡張UIが
/// ライブに連携するデモ」になる。
///
/// 仕組み:
///   1. WebUI ウィジェット（sample-waveform-control.js）がスライダー操作のたびに
///      NGOL.pushLiveParams(自分のnodeInstanceId, { frequency, amplitude }) を呼ぶ
///      → LiveParamStore に書き込まれる（自ノード宛てのみ）
///   2. 本ノードの OnUpdate が ctx.GetLiveParam で毎フレームその値を読み取り、
///      ctx.PushLiveValue で出力ポートへ流す
///   3. PushLiveValue は接続先ノードの SnapshotStore スロットへライブ書き込みする
///      （GetDownstreamConnections 経由）ため、通常のポート接続を伝ってリアルタイムに
///      下流ノードへ届く（下流側は ctx.SnapshotStore.GetSnapshot で毎フレーム読む想定）
///
/// 出力:
///   frequency → スライダーで設定した波の速さ（既定 1.3、範囲 0.2〜5.0）
///   amplitude → スライダーで設定した振幅（既定 0.6、範囲 0.1〜1.5）
///
/// 主な使い方:
///   1. sample-waveform-control.js を WebUI/plugins/ に配置して WebUI をリロード
///   2. 本ノードを配置し、frequency/amplitude 出力を LiveWaveformNode の同名入力へ接続
///   3. 両ノードを実行した状態でスライダーを動かすと波形がリアルタイムに変化する
/// </summary>

using System.Globalization;
using NodeGraphModLab.NodeAPI;

[NodeType("sample.webui_waveform_control", "Samples/WebUI", "Waveform Control",
    Description = "Streams frequency/amplitude values set by WebUI sliders to connected nodes " +
                   "in real time. Pairs with LiveWaveformNode to demonstrate live cooperation " +
                   "between two WebUI-extended node instances.")]
[NodePort("frequency", PortDirection.Output, "number")]
[NodePort("amplitude", PortDirection.Output, "number")]
[NodeWebUi("sample.webui.waveform_control")]
public sealed class WaveformControlNode : INode
{
    private const double PushIntervalSeconds = 1.0 / 30.0;
    private const double DefaultFrequency = 1.3;
    private const double DefaultAmplitude = 0.6;

    private static IPersistentRegistration? _reg;

    public void Execute(IExecutionContext ctx)
    {
        if (_reg?.IsActive == true) return; // 二重登録防止

        // 初期値: paramValues（グラフJSON直書き・スライダーの初期位置）から取得。
        // pushLiveParams が一度も呼ばれていない間は GetLiveParam のフォールバックとして使われる。
        double initialFrequency = ParseParam(ctx, "frequency", DefaultFrequency);
        double initialAmplitude = ParseParam(ctx, "amplitude", DefaultAmplitude);

        double lastPush = -1.0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _reg = ctx.RegisterPersistent(new PersistentCallbacks
        {
            OnUpdate = () =>
            {
                var t = sw.Elapsed.TotalSeconds;
                if (t - lastPush < PushIntervalSeconds) return;
                lastPush = t;

                double frequency = ctx.GetLiveParam("frequency", initialFrequency);
                double amplitude = ctx.GetLiveParam("amplitude", initialAmplitude);

                ctx.PushLiveValue("frequency", frequency);
                ctx.PushLiveValue("amplitude", amplitude);
            },
        });

        ctx.SetPortValue("frequency", initialFrequency);
        ctx.SetPortValue("amplitude", initialAmplitude);
    }

    private static double ParseParam(IExecutionContext ctx, string name, double fallback)
    {
        var s = ctx.GetParam<string>(name);
        if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        return fallback;
    }
}
