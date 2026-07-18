using System.Reflection;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Core.Extensions;

internal sealed class ExtensionLoadContext : IExtensionContext
{
    private readonly ExtensionHost _host;
    private readonly NodeRegistry _registry;
    private readonly PersistentNodeRunner _runner;
    private readonly NgolNodeLogger _logger;

    public string ExtensionId { get; }
    public string NgolRoot { get; }
    public string ExtensionDirectory { get; }
    public INodeLogger Logger => _logger;

    public ExtensionLoadContext(
        ExtensionHost host,
        string extensionId,
        string ngolRoot,
        string extensionDirectory,
        INgolLogger log,
        NodeRegistry registry,
        PersistentNodeRunner runner)
    {
        _host = host;
        ExtensionId = extensionId;
        NgolRoot = ngolRoot;
        ExtensionDirectory = extensionDirectory;
        _logger = new NgolNodeLogger(log);
        _registry = registry;
        _runner = runner;
    }

    public void RegisterNodes(Assembly assembly) => _registry.RegisterAssembly(assembly);

    public void AddAssemblyResolvePath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        _host.AddAssemblyResolvePath(directory);
    }

    public void RegisterService(Type serviceType, object implementation, ExtensionServiceScope scope)
    {
        _host.ServiceRegistry.Register(ExtensionId, serviceType, implementation, scope);
    }

    public void RegisterPersistentTick(IExtensionPersistentWork work)
    {
        var nodeId = "ext:" + ExtensionId;
        _runner.Register(nodeId, ExtensionId, "", new PersistentCallbacks
        {
            OnUpdate = work.OnUpdate,
            OnStop = work.OnStop,
        });
    }

    public void RegisterCapability(string capabilityId, string? version = null)
    {
        _host.RegisterCapability(ExtensionId, capabilityId, version);
    }
}
