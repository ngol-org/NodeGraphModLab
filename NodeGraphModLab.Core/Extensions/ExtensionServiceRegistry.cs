namespace NodeGraphModLab.Core.Extensions;

/// <summary>
/// Extension が <see cref="NodeGraphModLab.NodeAPI.IExtensionContext.RegisterService"/> した
/// interface → 実装の解決レジストリ。
/// </summary>
public sealed class ExtensionServiceRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<Type, ServiceEntry> _services = new();

    public void Register(string extensionId, Type serviceType, object implementation, NodeGraphModLab.NodeAPI.ExtensionServiceScope scope)
    {
        if (!serviceType.IsInstanceOfType(implementation))
            throw new ArgumentException($"Implementation does not implement {serviceType.FullName}", nameof(implementation));

        lock (_lock)
        {
            if (_services.TryGetValue(serviceType, out var existing) && existing.Implementation != null)
            {
                // 後勝ちだがログは ExtensionHost 側で出す
            }

            _services[serviceType] = new ServiceEntry(extensionId, implementation, scope);
        }
    }

    public T? GetService<T>() where T : class
    {
        lock (_lock)
        {
            if (_services.TryGetValue(typeof(T), out var entry) && entry.Implementation is T typed)
                return typed;
            return null;
        }
    }

    public void RemoveExtension(string extensionId)
    {
        lock (_lock)
        {
            var keys = _services.Where(kv => kv.Value.ExtensionId == extensionId).Select(kv => kv.Key).ToList();
            foreach (var key in keys)
            {
                if (_services[key].Scope == NodeGraphModLab.NodeAPI.ExtensionServiceScope.Extension)
                    _services.Remove(key);
            }
        }
    }

    public void Clear()
    {
        lock (_lock) { _services.Clear(); }
    }

    private sealed class ServiceEntry
    {
        public string ExtensionId { get; }
        public object? Implementation { get; }
        public NodeGraphModLab.NodeAPI.ExtensionServiceScope Scope { get; }

        public ServiceEntry(string extensionId, object? implementation, NodeGraphModLab.NodeAPI.ExtensionServiceScope scope)
        {
            ExtensionId = extensionId;
            Implementation = implementation;
            Scope = scope;
        }
    }
}
