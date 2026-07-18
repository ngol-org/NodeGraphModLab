using System.Reflection;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Core.Extensions;

public sealed class ExtensionHost
{
    private readonly INgolLogger _log;
    private readonly List<LoadedExtension> _loaded = new();
    private readonly HashSet<string> _resolvePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string ExtensionId, string CapabilityId, string? Version)> _capabilities = new();
    private readonly object _lock = new();
    private bool _resolveHandlerRegistered;

    public ExtensionServiceRegistry ServiceRegistry { get; } = new();

    public ExtensionHost(INgolLogger log) => _log = log;

    public IReadOnlyList<(string ExtensionId, string CapabilityId, string? Version)> Capabilities
    {
        get { lock (_lock) { return _capabilities.ToList(); } }
    }

    public IReadOnlyList<ExtensionManifestEntry> GetManifestEntries()
    {
        lock (_lock)
        {
            var map = new Dictionary<string, ExtensionManifestEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in _loaded)
            {
                map[ext.Manifest.Id] = new ExtensionManifestEntry
                {
                    Id = ext.Manifest.Id,
                    Version = ext.Manifest.Version,
                };
            }

            foreach (var (extensionId, capabilityId, version) in _capabilities)
            {
                if (!map.TryGetValue(extensionId, out var entry))
                {
                    entry = new ExtensionManifestEntry { Id = extensionId, Version = "" };
                    map[extensionId] = entry;
                }

                entry.Capabilities.Add(new ExtensionCapabilityEntry
                {
                    Id = capabilityId,
                    Version = version,
                });
            }

            return map.Values.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public void LoadAll(string ngolRoot, NodeRegistry registry, PersistentNodeRunner runner)
    {
        var extensionsDir = Path.Combine(ngolRoot, "Extensions");
        if (!Directory.Exists(extensionsDir))
        {
            _log.LogDebug("[ExtensionHost] no Extensions directory — skip");
            return;
        }

        foreach (var extensionDir in Directory.EnumerateDirectories(extensionsDir))
        {
            try { LoadOne(ngolRoot, extensionDir, registry, runner); }
            catch (Exception ex)
            {
                _log.LogError($"[ExtensionHost] failed to load '{extensionDir}': {ex.Message}");
            }
        }
    }

    public void UnloadAll()
    {
        List<LoadedExtension> snapshot;
        lock (_lock)
        {
            snapshot = new List<LoadedExtension>(_loaded);
            _loaded.Clear();
            _capabilities.Clear();
        }

        foreach (var ext in snapshot.Reverse<LoadedExtension>())
        {
            try
            {
                ext.Instance.Unload(ext.Context);
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[ExtensionHost] unload error '{ext.Manifest.Id}': {ex.Message}");
            }

            ServiceRegistry.RemoveExtension(ext.Manifest.Id);
        }
    }

    internal void AddAssemblyResolvePath(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!Directory.Exists(full)) return;

        lock (_lock)
        {
            if (!_resolvePaths.Add(full)) return;
        }

        EnsureResolveHandler();
        _log.LogInfo($"[ExtensionHost] assembly resolve path: {full}");
    }

    internal void RegisterCapability(string extensionId, string capabilityId, string? version)
    {
        lock (_lock) { _capabilities.Add((extensionId, capabilityId, version)); }
    }

    internal Assembly? TryResolveAssembly(AssemblyName assemblyName)
    {
        var shortName = assemblyName.Name;
        if (string.IsNullOrEmpty(shortName)) return null;

        string[] paths;
        lock (_lock) { paths = _resolvePaths.ToArray(); }

        foreach (var dir in paths)
        {
            try
            {
                var path = Path.Combine(dir, shortName + ".dll");
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            catch { /* try next */ }
        }

        return null;
    }

    private void LoadOne(string ngolRoot, string extensionDir, NodeRegistry registry, PersistentNodeRunner runner)
    {
        var manifestPath = Path.Combine(extensionDir, "extension.json");
        if (!ExtensionManifest.TryLoad(manifestPath, out var manifest, out var error) || manifest == null)
        {
            _log.LogWarning($"[ExtensionHost] skip '{extensionDir}': {error}");
            return;
        }

        if (!manifest.Enabled)
        {
            _log.LogInfo($"[ExtensionHost] disabled: {manifest.Id}");
            return;
        }

        if (manifest.ApiVersion != ExtensionManifest.SupportedApiVersion)
        {
            _log.LogWarning($"[ExtensionHost] skip '{manifest.Id}': apiVersion {manifest.ApiVersion} != {ExtensionManifest.SupportedApiVersion}");
            return;
        }

        var tfm = GetRuntimeTfm();
        var libDir = Path.Combine(extensionDir, "lib", tfm);
        if (manifest.Libraries?.Preload != false && Directory.Exists(libDir))
            PreloadLibraries(libDir);

        AddAssemblyResolvePath(libDir);
        AddAssemblyResolvePath(extensionDir);

        var entryPath = Path.Combine(extensionDir, manifest.EntryAssembly);
        if (!File.Exists(entryPath))
        {
            _log.LogWarning($"[ExtensionHost] skip '{manifest.Id}': entry assembly not found: {entryPath}");
            return;
        }

        var entryAssembly = Assembly.LoadFrom(entryPath);
        var entryType = entryAssembly.GetType(manifest.EntryType, throwOnError: false);
        if (entryType == null || !typeof(INgolExtension).IsAssignableFrom(entryType))
        {
            _log.LogWarning($"[ExtensionHost] skip '{manifest.Id}': entry type not found or not INgolExtension: {manifest.EntryType}");
            return;
        }

        var instance = (INgolExtension?)Activator.CreateInstance(entryType);
        if (instance == null)
        {
            _log.LogWarning($"[ExtensionHost] skip '{manifest.Id}': failed to create entry type");
            return;
        }

        var context = new ExtensionLoadContext(this, manifest.Id, ngolRoot, extensionDir, _log, registry, runner);
        instance.Load(context);

        RegisterExtensionNodes(manifest, extensionDir, registry);

        foreach (var cap in manifest.Capabilities)
            RegisterCapability(manifest.Id, cap, manifest.Version);

        lock (_lock)
        {
            _loaded.Add(new LoadedExtension(manifest, instance, context));
        }

        _log.LogInfo($"[ExtensionHost] loaded: {manifest.Id} v{manifest.Version}");
    }

    private static void RegisterExtensionNodes(ExtensionManifest manifest, string extensionDir, NodeRegistry registry)
    {
        var nodes = manifest.Nodes;
        if (nodes == null) return;

        var mode = nodes.Mode?.Trim() ?? "dll";
        if (!string.Equals(mode, "dll", StringComparison.OrdinalIgnoreCase))
            return;

        var nodesDir = Path.Combine(extensionDir, nodes.Directory ?? "nodes");
        if (!Directory.Exists(nodesDir)) return;

        foreach (var dll in Directory.EnumerateFiles(nodesDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var asm = Assembly.LoadFrom(dll);
                registry.RegisterAssembly(asm);
            }
            catch { /* optional node dll */ }
        }
    }

    private void PreloadLibraries(string libDir)
    {
        foreach (var dll in Directory.EnumerateFiles(libDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try { Assembly.LoadFrom(dll); }
            catch (Exception ex) { _log.LogWarning($"[ExtensionHost] preload failed: {Path.GetFileName(dll)} — {ex.Message}"); }
        }
    }

    private void EnsureResolveHandler()
    {
        if (_resolveHandlerRegistered) return;
        _resolveHandlerRegistered = true;

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            try
            {
                var name = new AssemblyName(args.Name);
                return TryResolveAssembly(name);
            }
            catch { return null; }
        };
    }

    private static string GetRuntimeTfm()
    {
#if NET6_0_OR_GREATER
        return "net6.0";
#else
        return "net462";
#endif
    }

    private sealed class LoadedExtension
    {
        public ExtensionManifest Manifest { get; }
        public INgolExtension Instance { get; }
        public ExtensionLoadContext Context { get; }

        public LoadedExtension(ExtensionManifest manifest, INgolExtension instance, ExtensionLoadContext context)
        {
            Manifest = manifest;
            Instance = instance;
            Context = context;
        }
    }
}
