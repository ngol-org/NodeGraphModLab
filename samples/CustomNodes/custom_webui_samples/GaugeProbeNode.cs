/// <summary>
/// 数値をスナップショットに保存し、WebUI のゲージウィジェットで可視化するサンプルノード。
/// sample.webui.gauge WebUI プラグイン（samples/WebUIPlugins/sample-gauge.js）との
/// 組み合わせを前提とした使用例。ExtraJson でゲージ最大値をプラグインへ渡す。
///
/// 入力:
///   value → 表示する数値
///
/// 出力:
///   value → 入力をそのまま透過出力
///
/// 主な使い方:
///   1. sample-gauge.js を WebUI/plugins/ に配置して WebUI をリロード
///   2. value に数値を接続（または直接入力）してノードを実行
///   3. ノード内にゲージバーが表示される
/// </summary>

using System.Globalization;
using NodeGraphModLab.NodeAPI;

[NodeType("sample.webui_gauge_probe", "Samples/WebUI", "Gauge Probe",
    Description = "Saves a number as a snapshot and visualizes it with the sample gauge WebUI plugin.")]
[NodePort("value", PortDirection.Input, "number")]
[NodePort("value_out", PortDirection.Output, "number")]
[NodeWebUi("sample.webui.gauge", OptionsFromSnapshot = "value", ExtraJson = "{\"max\":\"100\"}")]
public sealed class GaugeProbeNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var raw = ctx.GetPortValue("value") ?? ctx.GetParam<string>("value");
        double value = 0;
        if (raw is double d) value = d;
        else if (raw is float f) value = f;
        else if (raw is int i) value = i;
        else if (raw is string s) double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        // ゲージウィジェットは snapshotBadge.valueString からこの値を読む
        ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "value",
            value.ToString(CultureInfo.InvariantCulture));

        ctx.SetPortValue("value_out", value);
    }
}
