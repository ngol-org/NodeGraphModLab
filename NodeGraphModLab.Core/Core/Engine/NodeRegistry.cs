using System.Reflection;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Core.Engine;

/// <summary>
/// plugins/ フォルダ内の DLL をスキャンして [NodeType] 付きクラスを登録する。
/// カスタムノード DLL を plugins/ に配置するだけで自動登録される。
/// </summary>
public sealed class NodeRegistry
{
    private readonly Dictionary<string, NodeTypeDescriptor> _nodes = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public IReadOnlyDictionary<string, NodeTypeDescriptor> All
    {
        get { lock (_lock) { return new Dictionary<string, NodeTypeDescriptor>(_nodes); } }
    }

    /// <summary>
    /// 指定ディレクトリ以下の DLL をスキャンしてノードを登録する。
    /// 既存の登録はクリアされずに追加される。
    /// </summary>
    public void Scan(string directory, Func<string, bool>? includeFile = null)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var dllPath in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            if (includeFile != null && !includeFile(dllPath)) continue;
            TryLoadAssembly(dllPath);
        }
    }

    /// <summary>Assembly をスキャンしてノードを登録する。</summary>
    public void RegisterAssembly(Assembly assembly)
    {
        foreach (var type in SafeGetTypes(assembly))
        {
            TryRegisterType(type);
        }
    }

    /// <summary>
    /// Assembly をスキャンして新たに登録されたノードタイプ ID のリストを返す。
    /// Roslyn 動的コンパイル後の登録確認に使用する。
    /// </summary>
    public List<string> ScanAssembly(Assembly assembly)
    {
        var before = new HashSet<string>();
        lock (_lock) { before = new HashSet<string>(_nodes.Keys); }

        RegisterAssembly(assembly);

        lock (_lock)
        {
            return _nodes.Keys.Where(k => !before.Contains(k)).ToList();
        }
    }

    /// <summary>登録済みのノードをすべて削除する。レジストリ再構築時に使用する。</summary>
    public void Clear()
    {
        lock (_lock) { _nodes.Clear(); }
    }

    /// <summary>指定ノードタイプ ID の登録を削除する。</summary>
    public void Remove(string nodeTypeId)
    {
        lock (_lock) { _nodes.Remove(nodeTypeId); }
    }

    /// <summary>単一の Type を登録する。DLL 版の個別復元に使用する。</summary>
    public void RegisterType(Type type) => TryRegisterType(type);

    /// <summary>ノードタイプ ID で NodeTypeDescriptor を取得する。</summary>
    public NodeTypeDescriptor? Get(string nodeTypeId)
    {
        lock (_lock)
        {
            _nodes.TryGetValue(nodeTypeId, out var desc);
            return desc;
        }
    }

    /// <summary>登録済みの全ノードタイプを返す（カテゴリ順ソート）。</summary>
    public IEnumerable<NodeTypeDescriptor> GetAll()
    {
        lock (_lock)
        {
            return _nodes.Values.OrderBy(d => d.Category).ThenBy(d => d.DisplayName).ToList();
        }
    }

    /// <summary>ノードタイプ ID からインスタンスを生成する。</summary>
    public INode? CreateInstance(string nodeTypeId)
    {
        var desc = Get(nodeTypeId);
        if (desc == null) return null;

        try
        {
            return (INode?)Activator.CreateInstance(desc.ImplementationType);
        }
        catch
        {
            return null;
        }
    }

    // ---- 内部ヘルパー ----

    private void TryLoadAssembly(string dllPath)
    {
        try
        {
            var assembly = Assembly.LoadFrom(dllPath);
            RegisterAssembly(assembly);
        }
        catch
        {
            // 読み込み失敗 DLL は無視（非ノードDLL、依存関係不足など）
        }
    }

    private void TryRegisterType(Type type)
    {
        if (type.IsAbstract || type.IsInterface) return;
        if (!typeof(INode).IsAssignableFrom(type)) return;

        var nodeAttr = type.GetCustomAttribute<NodeTypeAttribute>();
        if (nodeAttr == null) return;

        var ports = type.GetCustomAttributes<NodePortAttribute>()
            .Select(p => new NodePortDefinition
            {
                Name = p.Name,
                Direction = p.Direction,
                DataType = p.DataType,
                IsRequired = p.IsRequired,
                Description = p.Description,
                ShowInlineEditor = p.ShowInlineEditor
            })
            .ToList();

        var customWebUiAttr = type.GetCustomAttribute<NodeWebUiAttribute>();
        var nodeVersion = string.IsNullOrWhiteSpace(nodeAttr.Version)
            ? "1.0.0"
            : nodeAttr.Version.Trim();

        var descriptor = new NodeTypeDescriptor
        {
            Id = nodeAttr.Id,
            Category = nodeAttr.Category,
            DisplayName = nodeAttr.DisplayName,
            Description = nodeAttr.Description,
            Version = nodeVersion,
            Ports = ports,
            ImplementationType = type,
            CustomWebUi = customWebUiAttr?.ToJson()
        };

        lock (_lock)
        {
            _nodes[descriptor.Id] = descriptor;
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
        catch { return Enumerable.Empty<Type>(); }
    }
}
