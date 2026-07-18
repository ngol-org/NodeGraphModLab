using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.BuiltinNodes.Logic;

/// <summary>
/// 入力文字列をパススルーするノード。WebUI プラグイン（ノード型 ID 上書き）と組み合わせることで
/// ノード全体を編集可能なテキストボックスとして表示できる。プラグイン未配置時は標準描画で動作する。
/// </summary>
[NodeType("ngol.logic.text_box", "Logic/String", "Text Box",
    Version = "1.0.0",
    Description = "Displays the input string in an editable text box and passes it through to the output.")]
[NodePort("text", PortDirection.Input, "string", ShowInlineEditor = true, Description = "Text to display (leave unconnected to type directly)")]
[NodePort("text", PortDirection.Output, "string", Description = "Passthrough of the (possibly edited) text")]
public sealed class TextBoxNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var text = ctx.GetPortValue("text") as string
                ?? ctx.GetPortValue("text")?.ToString() ?? "";
        ctx.SetPortValue("text", text);
        ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "text", text);
    }
}
