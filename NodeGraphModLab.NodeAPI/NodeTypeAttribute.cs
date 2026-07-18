namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// ノードタイプを識別するための属性。
/// INode を実装するクラスに付与することで、NodeRegistry に自動登録される。
/// </summary>
/// <example>
/// [NodeType("ngol.logic.add", "Logic/Math", "Add Numbers")]
/// public class AddNode : INode { ... }
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NodeTypeAttribute : Attribute
{
    /// <summary>
    /// グローバルに一意なノードタイプ ID。
    /// 推奨形式: "namespace.category.node_name" (例: "ngol.logic.add")
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// ノードエディタに表示するカテゴリ。スラッシュで階層化可能 (例: "Logic/Math")。
    /// </summary>
    public string Category { get; }

    /// <summary>ノードエディタに表示する表示名。</summary>
    public string DisplayName { get; }

    /// <summary>ノードの説明（ツールチップ等で表示）。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// ノード型のバージョン（SemVer想定）。
    /// 省略時は "1.0.0"。
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    public NodeTypeAttribute(string id, string category, string displayName)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Node type id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category must not be empty.", nameof(category));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName must not be empty.", nameof(displayName));

        Id = id;
        Category = category;
        DisplayName = displayName;
    }
}

/// <summary>
/// ノードのポート定義を宣言する属性。INode 実装クラスに複数付与できる。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class NodePortAttribute : Attribute
{
    public string Name { get; }
    public PortDirection Direction { get; }
    public string DataType { get; }
    public bool IsRequired { get; set; } = false;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// ノードキャンバス上でこのポートにインラインエディタを表示するかどうか（opt-in）。
    /// 入力・出力ポート共通の属性。true を指定し、かつ型がインライン対応（bool/number/string/enum/color）の場合のみ表示する。
    /// デフォルト（false）は非表示。出力ポートは、かつ入力ポートを持たないノードでのみ表示される。
    /// Inspector パネルでは ShowInlineEditor の値に関わらず常に編集可能。
    /// 属性の名前付き引数には Nullable&lt;bool&gt; を指定できない（CS0655）ため bool 型（既定 false）とする。
    /// </summary>
    public bool ShowInlineEditor { get; set; } = false;

    public NodePortAttribute(string name, PortDirection direction, string dataType)
    {
        Name = name;
        Direction = direction;
        DataType = dataType;
    }
}

/// <summary>ポートの方向（入力/出力）。</summary>
public enum PortDirection
{
    Input,
    Output
}

/// <summary>
/// ノードが WebUI で UI プラグインを使用することを宣言する属性。
/// WebUI のプラグインレジストリに登録された ID を指定する。
/// 対応プラグインが未登録の場合、WebUI は標準テキスト表示にフォールバックする。
/// </summary>
/// <example>
/// [NodeWebUi("ngol.webui.dropdown", OptionsFromSnapshot = "options", BindTo = "selected")]
/// public class StringArraySelectorNode : INode { ... }
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NodeWebUiAttribute : Attribute
{
    /// <summary>UI プラグインの一意識別子。</summary>
    public string PluginId { get; }

    /// <summary>スナップショットから選択肢を読むポート名（dropdown 系プラグイン用）。</summary>
    public string? OptionsFromSnapshot { get; set; }

    /// <summary>選択値をバインドする paramValues のキー名（dropdown 系プラグイン用）。</summary>
    public string? BindTo { get; set; }

    /// <summary>
    /// プラグイン固有の追加設定（JSON オブジェクト文字列）。
    /// WebUI には spec の "extra" フィールドとして渡される。
    /// 有効な JSON であることは宣言側の責務（不正な場合 WebUI は標準表示にフォールバックする）。
    /// </summary>
    public string? ExtraJson { get; set; }

    public NodeWebUiAttribute(string pluginId)
    {
        PluginId = pluginId;
    }

    /// <summary>WebUI プロトコル用 JSON 文字列を生成する。</summary>
    public string ToJson()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"pluginId\":\"");
        sb.Append(PluginId.Replace("\"", "\\\""));
        sb.Append('"');
        if (OptionsFromSnapshot is not null)
        {
            sb.Append(",\"optionsFromSnapshot\":\"");
            sb.Append(OptionsFromSnapshot.Replace("\"", "\\\""));
            sb.Append('"');
        }
        if (BindTo is not null)
        {
            sb.Append(",\"bindTo\":\"");
            sb.Append(BindTo.Replace("\"", "\\\""));
            sb.Append('"');
        }
        if (ExtraJson is not null)
        {
            sb.Append(",\"extra\":");
            sb.Append(ExtraJson);
        }
        sb.Append('}');
        return sb.ToString();
    }
}
