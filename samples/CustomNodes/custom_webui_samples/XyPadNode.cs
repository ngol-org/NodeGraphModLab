/// <summary>
/// WebUI の XY パッドで設定した座標 (0..1) を出力するサンプルノード。
/// sample.webui.xypad WebUI プラグイン（samples/WebUIPlugins/sample-fullnode.js）との
/// 組み合わせを前提とした、フルノード描画（nodeRenderer）の使用例。
/// ノードの内側描画全体がプラグイン JS 側で行われる。
///
/// 出力:
///   x → パッドの X 座標 (0..1)
///   y → パッドの Y 座標 (0..1)
///
/// 主な使い方:
///   1. sample-fullnode.js を WebUI/plugins/ に配置して WebUI をリロード
///   2. このノードを配置すると XY パッド付きのカスタム描画になる
///   3. パッドをドラッグして座標を設定 → 実行で x / y に出力される
/// </summary>

using System.Globalization;
using NodeGraphModLab.NodeAPI;

[NodeType("sample.webui_xy_pad", "Samples/WebUI", "XY Pad",
    Description = "Outputs the coordinates set on the sample XY pad WebUI plugin (0..1 each).")]
[NodePort("x", PortDirection.Output, "number")]
[NodePort("y", PortDirection.Output, "number")]
[NodeWebUi("sample.webui.xypad")]
public sealed class XyPadNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        // XY パッドプラグインは paramValues の x / y に文字列で書き込む
        double x = ParseParam(ctx, "x", 0.5);
        double y = ParseParam(ctx, "y", 0.5);

        ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "x", x.ToString(CultureInfo.InvariantCulture));
        ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "y", y.ToString(CultureInfo.InvariantCulture));
        ctx.SetPortValue("x", x);
        ctx.SetPortValue("y", y);
    }

    private static double ParseParam(IExecutionContext ctx, string name, double fallback)
    {
        var s = ctx.GetParam<string>(name);
        if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        return fallback;
    }
}
