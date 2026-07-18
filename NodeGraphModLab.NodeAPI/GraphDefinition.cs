using System.Text.Json;
using System.Text.Json.Serialization;

namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// ノードグラフ全体の定義（JSON保存・ロード用データモデル）。
/// </summary>
public sealed class NodeGraph
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "New Graph";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// グラフデータフォーマットのスキーマバージョン。
    /// 変更履歴:
    ///   (なし/空) = 初期版（nodes / connections / fragments / fragmentLinks）
    ///   "0.1.0"   = groups フィールド追加（2026-05-09）
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = NodeGraph.CurrentSchemaVersion;

    /// <summary>現在サポートするスキーマバージョン。</summary>
    public const string CurrentSchemaVersion = "0.2.0";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("nodes")]
    public List<NodeInstance> Nodes { get; set; } = new();

    [JsonPropertyName("connections")]
    public List<NodeConnection> Connections { get; set; } = new();

    [JsonPropertyName("fragments")]
    public List<FragmentDefinition> Fragments { get; set; } = new();

    [JsonPropertyName("fragmentLinks")]
    public List<FragmentLink> FragmentLinks { get; set; } = new();

    [JsonPropertyName("groups")]
    public List<NodeGroup> Groups { get; set; } = new();

    [JsonPropertyName("annotations")]
    public List<NodeAnnotation> Annotations { get; set; } = new();

    // ---- シリアライゼーション ヘルパー ----

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, NodeGraphJsonContext.Default.NodeGraph);
    }

    public static NodeGraph? FromJson(string json)
    {
        return JsonSerializer.Deserialize(json, NodeGraphJsonContext.Default.NodeGraph);
    }
}

/// <summary>グラフ内のノードの配置インスタンス。</summary>
public sealed class NodeInstance
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>対応するノードタイプID ([NodeType] 属性の Id)。</summary>
    [JsonPropertyName("nodeTypeId")]
    public string NodeTypeId { get; set; } = string.Empty;

    /// <summary>
    /// ノード追加時点のノード型バージョン。
    /// 旧グラフ互換のため省略可能（null）。
    /// </summary>
    [JsonPropertyName("nodeTypeVersion")]
    public string? NodeTypeVersion { get; set; }

    /// <summary>エディタ上の表示位置。</summary>
    [JsonPropertyName("position")]
    public NodePosition Position { get; set; } = new();

    /// <summary>エディタで設定したパラメータ値（固定値、ポート接続がない場合に使用）。</summary>
    [JsonPropertyName("paramValues")]
    public Dictionary<string, JsonElement> ParamValues { get; set; } = new();

    /// <summary>
    /// ユーザーが角ドラッグで手動リサイズしたサイズ。null = 自動サイズ（既定動作）。
    /// C# 側はこの値を実行ロジックに使わず、保存・再ロード時にそのまま往復させるためだけに保持する。
    /// </summary>
    [JsonPropertyName("size")]
    public NodeSize? Size { get; set; } = null;
}

/// <summary>グラフキャンバス上の注釈（付箋/コメント）。</summary>
public sealed class NodeAnnotation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("position")]
    public NodePosition Position { get; set; } = new();

    [JsonPropertyName("width")]
    public float Width { get; set; } = 200;

    [JsonPropertyName("height")]
    public float Height { get; set; } = 100;

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

/// <summary>グラフ内のノードグループ定義（ユーザー手動定義・視覚整理目的）。</summary>
public sealed class NodeGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Group";

    /// <summary>グループの用途・出力内容を記述する説明文（省略可能）。AI がグラフを読み書きする際の意図理解に使用。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("nodeInstanceIds")]
    public List<string> NodeInstanceIds { get; set; } = new();

    [JsonPropertyName("collapsed")]
    public bool Collapsed { get; set; } = false;

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

/// <summary>グラフ内の断片（Fragment）定義。ノードの論理グループ。</summary>
public sealed class FragmentDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Fragment";

    /// <summary>この断片に属するノードインスタンスIDのリスト。</summary>
    [JsonPropertyName("nodeInstanceIds")]
    public List<string> NodeInstanceIds { get; set; } = new();
}

/// <summary>
/// 断片間のリンク。スナップショットノードの出力を別断片の入力ポートへ接続する。
/// 次断片実行時にSnapshotStoreから値が取得されて入力として注入される。
/// </summary>
public sealed class FragmentLink
{
    /// <summary>スナップショットノードのインスタンスID（出力元断片内）。</summary>
    [JsonPropertyName("sourceSnapshotNodeInstanceId")]
    public string SourceSnapshotNodeInstanceId { get; set; } = "";

    [JsonPropertyName("sourcePortName")]
    public string SourcePortName { get; set; } = "value";

    /// <summary>値を受け取る側のノードインスタンスID（別断片内）。</summary>
    [JsonPropertyName("toNodeInstanceId")]
    public string ToNodeInstanceId { get; set; } = "";

    [JsonPropertyName("toPortName")]
    public string ToPortName { get; set; } = "";
}

/// <summary>ノード間のポート接続定義。</summary>
public sealed class NodeConnection
{
    [JsonPropertyName("fromNodeInstanceId")]
    public string FromNodeInstanceId { get; set; } = string.Empty;

    [JsonPropertyName("fromPortName")]
    public string FromPortName { get; set; } = string.Empty;

    [JsonPropertyName("toNodeInstanceId")]
    public string ToNodeInstanceId { get; set; } = string.Empty;

    [JsonPropertyName("toPortName")]
    public string ToPortName { get; set; } = string.Empty;
}

/// <summary>エディタ上のノード表示座標。</summary>
public sealed class NodePosition
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }
}

/// <summary>ユーザーが手動リサイズしたノードのサイズ。</summary>
public sealed class NodeSize
{
    [JsonPropertyName("width")]
    public float Width { get; set; }

    [JsonPropertyName("height")]
    public float Height { get; set; }
}

/// <summary>NodeTypeDescriptor: NodeRegistry が保持するノードタイプのメタデータ。</summary>
public sealed class NodeTypeDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public List<NodePortDefinition> Ports { get; init; } = new();
    public Type ImplementationType { get; init; } = typeof(object);
    /// <summary>NodeWebUiAttribute.ToJson() の結果。null = 標準表示。</summary>
    public string? CustomWebUi { get; init; } = null;
}

/// <summary>ノードのポート定義（Registry に登録されたメタデータ）。</summary>
public sealed class NodePortDefinition
{
    public string Name { get; init; } = string.Empty;
    public PortDirection Direction { get; init; }
    public string DataType { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string Description { get; init; } = string.Empty;
    /// <summary>入力・出力ポート共通のインラインエディタ表示制御（opt-in）。true のときのみ表示、既定は false（非表示）。</summary>
    public bool ShowInlineEditor { get; init; } = false;
}

/// <summary>System.Text.Json ソース生成コンテキスト（AOT/IL2CPP 対応）。</summary>
[JsonSerializable(typeof(NodeGraph))]
[JsonSerializable(typeof(NodeInstance))]
[JsonSerializable(typeof(NodeConnection))]
[JsonSerializable(typeof(NodePosition))]
[JsonSerializable(typeof(NodeSize))]
[JsonSerializable(typeof(NodeGroup))]
[JsonSerializable(typeof(List<NodeGroup>))]
[JsonSerializable(typeof(NodeAnnotation))]
[JsonSerializable(typeof(List<NodeAnnotation>))]
[JsonSerializable(typeof(FragmentDefinition))]
[JsonSerializable(typeof(FragmentLink))]
[JsonSerializable(typeof(List<FragmentDefinition>))]
[JsonSerializable(typeof(List<FragmentLink>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class NodeGraphJsonContext : JsonSerializerContext { }
