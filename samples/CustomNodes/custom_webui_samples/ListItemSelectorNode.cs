/// <summary>
/// List Item Selector — 3rdParty WebUI ペアサンプル（list-item-selector.js）。
///
/// 入力:
///   items → 文字列リスト（上流で list 化した IEnumerable。CSV 等の生テキストは上流で split して接続）
///
/// 出力:
///   items    → 入力 list のパススルー
///   index    → 選択インデックス（0-based、空リスト時 -1）
///   selected → 選択要素（string）
///
/// デプロイ:
///   1. 本 .cs を Nodes/CustomNodes/cs/ へ配置して compile_node
///   2. samples/WebUIPlugins/list-item-selector.js を WebUI/plugins/ へ配置して F5
/// </summary>

using System.Collections;
using System.Collections.Generic;
using NodeGraphModLab.NodeAPI;

[NodeType("ngol.snapshot.list_item_selector", "Fragment/Snapshot", "List Item Selector",
    Description = "Receives a list on items, lets you pick one item in WebUI, and outputs the list passthrough, selected index, and selected item.")]
[NodePort("items", PortDirection.Input, "list")]
[NodePort("items", PortDirection.Output, "list")]
[NodePort("index", PortDirection.Output, "int")]
[NodePort("selected", PortDirection.Output, "string")]
[NodeWebUi("ngol.webui.list_item_selector", OptionsFromSnapshot = "items", BindTo = "selected")]
public sealed class ListItemSelectorNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var raw = ctx.GetPortValue("items");

        var items = new List<string>();
        if (raw is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
                items.Add(item?.ToString() ?? "");
        }

        ctx.SetPortValue("items", raw);

        var json = ToJsonArray(items);
        ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "items", json);

        var selected = ctx.GetParam<string>("selected");
        if (string.IsNullOrEmpty(selected))
            selected = items.Count > 0 ? items[0] : "";

        var index = ResolveIndex(ctx, items, selected);
        if (index >= 0 && index < items.Count)
            selected = items[index];

        ctx.SetPortValue("selected", selected);
        ctx.SetPortValue("index", index);
        ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "selected", selected);
        ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "index", index.ToString());
    }

    private static int ResolveIndex(IExecutionContext ctx, List<string> items, string selected)
    {
        if (items.Count == 0)
            return -1;

        var indexParam = ctx.GetParam<string>("index");
        if (int.TryParse(indexParam, out var parsed)
            && parsed >= 0
            && parsed < items.Count)
            return parsed;

        var idx = items.IndexOf(selected);
        return idx >= 0 ? idx : 0;
    }

    private static string ToJsonArray(List<string> items)
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"');
            sb.Append(items[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
            sb.Append('"');
        }
        sb.Append(']');
        return sb.ToString();
    }
}
