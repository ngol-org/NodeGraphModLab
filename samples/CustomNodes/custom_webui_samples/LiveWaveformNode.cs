/// <summary>
/// 継続的に変化する数値を PushLiveValue でストリーミングし、WebUI のライブ波形
/// nodeRenderer プラグイン（samples/WebUIPlugins/sample-live-waveform.js）で
/// スクロール折れ線グラフとして描画するサンプルノード。
///
/// 入力（任意・未接続でも既定値で動作）:
///   frequency → 波の速さ（既定 1.3）
///   amplitude → 振幅（既定 0.6）
/// 上流に WaveformControlNode（sample.webui_waveform_control）を接続すると、
/// スライダー操作がリアルタイムに波形へ反映される。これは PushLiveValue が
/// 接続先ノードの SnapshotStore スロットへライブ書き込みする仕組み
/// （GetDownstreamConnections + PushLiveToSnapshot）を利用しており、
/// 通常のポート接続（GetPortValue、Execute 時に一度だけ評価）とは別経路になる。
///
/// 出力:
///   value → 2つの正弦波 + 微小ノイズを合成した数値（実行毎に継続更新）
///
/// 主な使い方:
///   1. sample-live-waveform.js を WebUI/plugins/ に配置して WebUI をリロード
///   2. このノードを配置して実行すると、ノード内側にライブ波形が描画され続ける
///   3. WaveformControlNode を追加して frequency/amplitude ポートに接続すると連携動作する
/// </summary>

using System;
using System.Diagnostics;
using NodeGraphModLab.NodeAPI;

[NodeType("sample.webui_live_waveform", "Samples/WebUI", "Live Waveform",
    Description = "Streams a continuously varying number and visualizes it as a live " +
                   "scrolling waveform via the sample nodeRenderer WebUI plugin. Optional " +
                   "frequency/amplitude inputs can be driven live by WaveformControlNode.")]
[NodePort("frequency", PortDirection.Input, "number", IsRequired = false)]
[NodePort("amplitude", PortDirection.Input, "number", IsRequired = false)]
[NodePort("value", PortDirection.Output, "number")]
[NodeWebUi("sample.webui.waveform")]
public sealed class LiveWaveformNode : INode
{
    private const double PushIntervalSeconds = 1.0 / 30.0;
    private const double DefaultFrequency = 1.3;
    private const double DefaultAmplitude = 0.6;

    private static IPersistentRegistration? _reg;

    public void Execute(IExecutionContext ctx)
    {
        if (_reg?.IsActive == true) return; // 二重登録防止

        var sw = Stopwatch.StartNew();
        var rng = new Random();
        var lastPush = 0.0;

        // 初期値: 通常のポート接続（未接続なら paramValues）から一度だけ取得
        double initialFrequency = ToDouble(ctx.GetPortValue("frequency"), DefaultFrequency);
        double initialAmplitude = ToDouble(ctx.GetPortValue("amplitude"), DefaultAmplitude);

        _reg = ctx.RegisterPersistent(new PersistentCallbacks
        {
            OnUpdate = () =>
            {
                var t = sw.Elapsed.TotalSeconds;
                if (t - lastPush < PushIntervalSeconds) return;
                lastPush = t;

                // 毎フレーム: 上流の WaveformControlNode が PushLiveValue でライブ書き込みした
                // 自分の SnapshotStore スロットを読む（接続時のみ更新される。未接続なら初期値のまま）
                double frequency = ToDouble(
                    ctx.SnapshotStore?.GetSnapshot(ctx.NodeInstanceId, "frequency"), initialFrequency);
                double amplitude = ToDouble(
                    ctx.SnapshotStore?.GetSnapshot(ctx.NodeInstanceId, "amplitude"), initialAmplitude);

                double v = Math.Sin(t * frequency) * amplitude
                         + Math.Sin(t * frequency * 2.85 + 1.0) * amplitude * 0.4
                         + (rng.NextDouble() - 0.5) * 0.1 * amplitude;

                ctx.PushLiveValue("value", v);
            },
        });

        ctx.SetPortValue("value", 0.0);
    }

    private static double ToDouble(object? value, double fallback) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        _ => fallback,
    };
}
