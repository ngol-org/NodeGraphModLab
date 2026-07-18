using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.Core.Extensions;
using NodeGraphModLab.Core.KVStore;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Server;

namespace NodeGraphModLab;

/// <summary>
/// NGOL コア起動・ライフサイクル管理クラス。
/// 特定のホストフレームワークやコンポーネントモデルに依存しないため、様々なホストで使用できる。
/// MonoBehaviour モード: Initialize() → Tick() を毎フレーム呼ぶ。ホスト固有の追加フェーズは DrainPhase(string) で排出する。
/// Direct モード: Initialize() のみ。内部でドレインスレッドを起動する。
/// </summary>
public sealed class NgolRuntime : IDisposable
{
    private readonly INgolLogger _log;
    private readonly NgolRuntimeOptions _options;

    private NodeRegistry? _nodeRegistry;
    private GraphExecutor? _graphExecutor;
    private GraphServer? _graphServer;
    private PersistentNodeRunner? _runner;
    private IKVStore? _store;
    private string? _graphSaveDir;
    // 監視対象ディレクトリ（プライマリ Nodes/CustomNodes/cs + customNodeDirectories）ごとの
    // FileSystemWatcher（cs/rsp/srclist の3種）一覧
    private readonly Dictionary<string, List<FileSystemWatcher>> _watchersByDir = new(StringComparer.OrdinalIgnoreCase);
    private ExtensionHost? _extensionHost;
    private Thread? _drainThread;
    private bool _disposed;

    private bool _needsGcWorkaround;
    private int _gcCounter;
    private const int GcInterval = 60;
    private const int HotReloadDebounceMs = 500;

    private readonly ConcurrentDictionary<string, DateTime> _pendingRecompile = new();
    private readonly ConcurrentDictionary<string, string> _scriptNodeId = new();

    // .srclist で解決済みの追加ソースファイル一覧（ノード.csパス→解決済み絶対パス一覧、自身を含む）
    private readonly ConcurrentDictionary<string, List<string>> _srclistResolved = new(StringComparer.OrdinalIgnoreCase);
    // 共有ファイルパス → それを参照している依存ノード.csパス一覧（逆引き）
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _sharedFileDependents = new(StringComparer.OrdinalIgnoreCase);

    public IKVStore? Store => _store;

    /// <summary>ホスト初期化時、GCワークアラウンドの要否を判定した後に呼ぶ。</summary>
    public void SetGcWorkaround(bool enable) => _needsGcWorkaround = enable;

    public NgolRuntime(INgolLogger log, NgolRuntimeOptions? options = null)
    {
        _log = log;
        _options = options ?? new NgolRuntimeOptions();
    }

    public void Initialize(string pluginDir)
    {
        _needsGcWorkaround = _options.EnableGcWorkaround;

        _log.LogInfo($"[NgolRuntime] initializing (direct={_options.EnableDirectMode} gcWorkaround={_needsGcWorkaround})");

        NgolConfig.Load(pluginDir, _log);
        ConnectionAuthToken.Initialize(pluginDir, NgolConfig.RequireAuthToken, NgolConfig.Port, _log);
        PreloadRoslynAssemblies(pluginDir);
        _store = CreateKVStore(pluginDir);

        _nodeRegistry = new NodeRegistry();
        _nodeRegistry.Scan(pluginDir, IsNotUnderExtensionsFolder);

        _graphExecutor = new GraphExecutor(_nodeRegistry);
        _runner = new PersistentNodeRunner();

        _extensionHost = new ExtensionHost(_log);
        _extensionHost.LoadAll(pluginDir, _nodeRegistry, _runner);

        var graphSaveDir = Path.Combine(pluginDir, "Graphs");
        _graphSaveDir = graphSaveDir;
        var webUiDir = Path.Combine(pluginDir, "WebUI");
        var dynamicNodesDir = Path.Combine(pluginDir, "dynamic-nodes");
        var nodesDir = Path.Combine(pluginDir, "Nodes", "CustomNodes", "cs");
        var nodePacksDir = Path.Combine(pluginDir, "Nodes", "CustomNodes", "dll");

        Directory.CreateDirectory(nodesDir);
        Directory.CreateDirectory(nodePacksDir);

        var extraNodeDirs = ResolveCustomNodeDirectories(NgolConfig.CustomNodeDirectories, nodesDir, _log);

        RoslynCompiler.LoadPersistedNodes(dynamicNodesDir, _nodeRegistry, _log);
        var scriptsLoadTask = Task.Run(async () =>
        {
            await LoadCustomScriptsAsync(nodesDir);
            foreach (var dir in extraNodeDirs) await LoadCustomScriptsAsync(dir);
        });

        StartScriptsWatcher(nodesDir, isPrimary: true);
        foreach (var dir in extraNodeDirs) StartScriptsWatcher(dir, isPrimary: false);

        _graphServer = new GraphServer(
            NgolConfig.Port,
            _nodeRegistry,
            _graphExecutor,
            _log,
            graphSaveDir,
            webUiDir,
            _runner,
            dynamicNodesDir,
            nodesDir,
            nodePacksDir,
            _scriptNodeId,
            _store,
            _options.PluginVersion,
            _options.GameName,
            _extensionHost.ServiceRegistry,
            _extensionHost,
            _options.RuntimeType);
        _graphServer.Start();

        _ = scriptsLoadTask.ContinueWith(_ => RunStartupAutoExecution(), TaskScheduler.Default);

        _log.LogInfo("[NgolRuntime] initialized");

        if (_options.EnableDirectMode)
        {
            _drainThread = new Thread(DrainLoop) { IsBackground = true, Name = "NGOL-Drain" };
            _drainThread.Start();
        }
    }

    // ---- MonoBehaviour ライフサイクル ----

    public void Tick()
    {
        var now = DateTime.Now;
        foreach (var kv in _pendingRecompile)
        {
            if ((now - kv.Value).TotalMilliseconds >= HotReloadDebounceMs)
            {
                if (_pendingRecompile.TryRemove(kv.Key, out _))
                {
                    var path = kv.Key;
                    _ = Task.Run(async () => await HotReloadPathAsync(path));
                }
            }
        }

        try { _graphServer?.DrainPendingExecutions(); }
        catch (Exception ex) { _log.LogError("GraphServer drain error: " + ex.Message); }

        try { _runner?.DrainUpdate(); }
        catch (Exception ex) { _log.LogError("PersistentNodeRunner.DrainUpdate error: " + ex.Message); }

        if (_needsGcWorkaround)
        {
            _gcCounter++;
            if (_gcCounter >= GcInterval)
            {
                _gcCounter = 0;
                GC.Collect();
            }
        }
    }

    /// <summary>
    /// ホスト固有の拡張フェーズ（例: Unityブリッジの "Unity.OnGUI"）を排出する。
    /// ホストは自身のライフサイクルの適切なタイミングで、フェーズ名を決めて呼び出す。
    /// </summary>
    public void DrainPhase(string phaseName)
    {
        try { _runner?.DrainPhase(phaseName); }
        catch (Exception ex) { _log.LogError($"PersistentNodeRunner.DrainPhase({phaseName}) error: " + ex.Message); }
    }

    // ---- Direct モードドレインループ ----

    private void DrainLoop()
    {
        _options.DirectModeDrainSetup?.Invoke();

        while (true)
        {
            try
            {
                Thread.Sleep(NgolConfig.DirectModeIntervalMs);

                var now = DateTime.Now;
                foreach (var kv in _pendingRecompile)
                {
                    if ((now - kv.Value).TotalMilliseconds >= 500)
                    {
                        if (_pendingRecompile.TryRemove(kv.Key, out DateTime _))
                        {
                            var path = kv.Key;
                            _ = Task.Run(async () => await HotReloadPathAsync(path));
                        }
                    }
                }

                _graphServer?.DrainPendingExecutions();
                _runner?.DrainUpdate();
            }
            catch (ThreadInterruptedException) { break; }
            catch (Exception ex) { _log.LogError($"[Direct] DrainLoop error: {ex.Message}"); }
        }
    }

    // ---- スクリプトホットリロード ----

    /// <summary>
    /// 指定ディレクトリに対して .cs/.rsp/.srclist の3種の FileSystemWatcher を起動する。
    /// isPrimary=false（customNodeDirectories 由来の追加ディレクトリ）の場合のみ、
    /// ディレクトリ自体が消失したときに Error イベント経由でノード登録解除を行う。
    /// </summary>
    private void StartScriptsWatcher(string scriptsDir, bool isPrimary)
    {
        try
        {
            ErrorEventHandler? onError = isPrimary
                ? null
                : (_, __) => OnCustomNodeDirectoryLost(scriptsDir);

            var watchers = new List<FileSystemWatcher>
            {
                CreateWatcher(scriptsDir, "*.cs", onError),
                CreateWatcher(scriptsDir, "*.rsp", onError),
                CreateWatcher(scriptsDir, "*.srclist", onError)
            };
            _watchersByDir[scriptsDir] = watchers;
            _log.LogInfo("[Scripts] Hot-reload watcher started: " + scriptsDir);
        }
        catch (Exception ex)
        {
            _log.LogWarning("[Scripts] Failed to start watcher: " + ex.Message);
        }
    }

    private FileSystemWatcher CreateWatcher(string scriptsDir, string filter, ErrorEventHandler? onError)
    {
        var watcher = new FileSystemWatcher(scriptsDir, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
            IncludeSubdirectories = true
        };
        watcher.Changed += (_, e) => _pendingRecompile[e.FullPath] = DateTime.Now;
        watcher.Created += (_, e) => OnScriptFileCreated(e.FullPath);
        watcher.Renamed += (_, e) => _pendingRecompile[e.FullPath] = DateTime.Now;
        watcher.Deleted += OnScriptFileDeleted;
        if (onError != null) watcher.Error += onError;
        return watcher;
    }

    /// <summary>
    /// customNodeDirectories 由来の追加ディレクトリが実行中に消失した際、
    /// そのディレクトリ配下のファイルから読み込まれていたノードをレジストリから登録解除する。
    /// </summary>
    private void OnCustomNodeDirectoryLost(string dir)
    {
        // 多重発火（cs/rsp/srclist の3ウォッチャー分）に対する冪等ガード
        if (!_watchersByDir.TryGetValue(dir, out var watchers)) return;
        _watchersByDir.Remove(dir);

        foreach (var w in watchers) { try { w.Dispose(); } catch { } }

        var prefix = NormalizeDirForPrefixMatch(dir);
        var affected = _scriptNodeId
            .Where(kv => kv.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var nodeTypeId in affected)
        {
            _scriptNodeId.TryRemove(nodeTypeId, out _);
            _nodeRegistry?.Remove(nodeTypeId);
            TryRestoreBuiltinNode(nodeTypeId);
        }

        _graphServer?.BroadcastNodeListUpdated(affected.Count > 0 ? affected[0] : null);
        _log.LogWarning($"[Scripts] Custom node directory lost: {dir} — {affected.Count} node(s) unregistered");
    }

    private static string NormalizeDirForPrefixMatch(string dir)
    {
        var full = Path.GetFullPath(dir);
        var endsWithSeparator = full.Length > 0 && full[full.Length - 1] == Path.DirectorySeparatorChar;
        return endsWithSeparator ? full : full + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// ngol-config.json の customNodeDirectories を検証・正規化する（純粋関数、単体テスト用に internal 公開）。
    /// primary（Nodes/CustomNodes/cs）と重複するエントリ・存在しないディレクトリは除外する。
    /// </summary>
    internal static List<string> ResolveCustomNodeDirectories(IEnumerable<string> configured, string primaryDir, INgolLogger log)
    {
        var primaryFull = NormalizeDirForPrefixMatch(primaryDir);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryFull };
        var result = new List<string>();

        foreach (var raw in configured)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string full;
            try { full = NormalizeDirForPrefixMatch(raw); }
            catch (Exception ex)
            {
                log.LogWarning($"[Config] customNodeDirectories entry is not a valid path, skipping: {raw} ({ex.Message})");
                continue;
            }

            if (!seen.Add(full)) continue; // primary またはこれまでのエントリと重複

            if (!Directory.Exists(full))
            {
                log.LogWarning($"[Config] Custom node directory not found, skipping: {raw}");
                continue;
            }

            result.Add(full);
        }

        return result;
    }

    private void OnScriptFileCreated(string fullPath)
    {
        _pendingRecompile[fullPath] = DateTime.Now;

        // .cs が新規作成された際、同名の .srclist が既に存在していればインデックスに登録する
        // （.srclist を先に書いてから .cs を書く順序に対応）
        if (string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            var srclistPath = Path.ChangeExtension(fullPath, ".srclist");
            if (File.Exists(srclistPath))
            {
                try { RebuildSrclistIndex(srclistPath); }
                catch (Exception ex) { _log.LogWarning($"[Scripts] Failed to index srclist {srclistPath}: {ex.Message}"); }
            }
        }
    }

    private void OnScriptFileDeleted(object sender, FileSystemEventArgs e)
    {
        _pendingRecompile.TryRemove(e.FullPath, out DateTime _dt);
        var ext = Path.GetExtension(e.FullPath);

        if (string.Equals(ext, ".srclist", StringComparison.OrdinalIgnoreCase))
        {
            RemoveSrclistIndex(e.FullPath);
            var nodeCsPath = GetNodeCsPathForSrclist(e.FullPath);
            _log.LogInfo($"[Scripts] srclist deleted: {Path.GetFileName(e.FullPath)} — reverting to single-file compile");
            if (File.Exists(nodeCsPath))
                _ = Task.Run(async () => await CompileScriptFileAsync(nodeCsPath, isHotReload: true));
            return;
        }

        if (string.Equals(ext, ".rsp", StringComparison.OrdinalIgnoreCase))
        {
            var nodeCsPath = Path.ChangeExtension(e.FullPath, ".cs");
            _log.LogInfo($"[Scripts] rsp deleted: {Path.GetFileName(e.FullPath)} — reverting to default compile options");
            if (File.Exists(nodeCsPath))
                _ = Task.Run(async () => await CompileScriptFileAsync(nodeCsPath, isHotReload: true));
            return;
        }

        var keysToRemove = _scriptNodeId.Where(kv => kv.Value == e.FullPath).Select(kv => kv.Key).ToList();
        foreach (var k in keysToRemove) _scriptNodeId.TryRemove(k, out string _s);
        var deletedNodeId = keysToRemove.FirstOrDefault();
        _log.LogInfo($"[Scripts] Deleted: {Path.GetFileName(e.FullPath)} (nodeId={deletedNodeId ?? "unknown"}) — rebuilding registry");
        _ = Task.Run(() => RebuildRegistryAsync(deletedNodeId));
    }

    private Task RebuildRegistryAsync(string? deletedNodeId)
    {
        try
        {
            if (deletedNodeId != null)
            {
                _nodeRegistry?.Remove(deletedNodeId);
                TryRestoreBuiltinNode(deletedNodeId);
            }
            _graphServer?.BroadcastNodeListUpdated(deletedNodeId);
            _log.LogInfo($"[Scripts] Registry updated after deletion: nodeId={deletedNodeId ?? "unknown"}");
        }
        catch (Exception ex) { _log.LogError($"[Scripts] Registry update error: {ex.Message}"); }
        return Task.CompletedTask;
    }

    private void TryRestoreBuiltinNode(string nodeTypeId)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            if (string.IsNullOrEmpty(asm.Location)) continue;
            try
            {
                foreach (var type in asm.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(INode).IsAssignableFrom(type)) continue;
                    var attr = type.GetCustomAttribute<NodeTypeAttribute>();
                    if (attr?.Id != nodeTypeId) continue;
                    _nodeRegistry?.RegisterType(type);
                    _log.LogInfo($"[Scripts] Restored builtin node from DLL: {nodeTypeId} ({asm.GetName().Name})");
                    return;
                }
            }
            catch { }
        }
        _log.LogDebug($"[Scripts] No builtin found for {nodeTypeId} (pure custom node removed)");
    }

    /// <summary>
    /// 変更されたファイル（.cs/.rsp/.srclist）を拡張子で分岐し、影響を受けるノードを再コンパイルする。
    /// .srclist の逆引きインデックス（_sharedFileDependents）により、共有ファイルが変更された
    /// 場合でもディレクトリの再スキャンなしにO(1)で依存ノードを特定できる。
    /// </summary>
    private async Task HotReloadPathAsync(string filePath)
    {
        if (_nodeRegistry == null) return;
        try
        {
            var ext = Path.GetExtension(filePath);

            if (string.Equals(ext, ".srclist", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(filePath)) return; // 削除は OnScriptFileDeleted 側で処理
                RebuildSrclistIndex(filePath);
                var nodeCsPath = GetNodeCsPathForSrclist(filePath);
                if (File.Exists(nodeCsPath))
                    await CompileScriptFileAsync(nodeCsPath, isHotReload: true);
                return;
            }

            if (string.Equals(ext, ".rsp", StringComparison.OrdinalIgnoreCase))
            {
                var nodeCsPath = Path.ChangeExtension(filePath, ".cs");
                if (File.Exists(nodeCsPath))
                    await CompileScriptFileAsync(nodeCsPath, isHotReload: true);
                return;
            }

            if (!File.Exists(filePath)) return;

            // 既に他ノードの .srclist から参照されている「既知の共有ファイル」かどうか
            var hasDependents = _sharedFileDependents.TryGetValue(filePath, out var deps) && !deps.IsEmpty;
            // 過去に自分自身がノードとして登録されたことがあるか（独立ノードとして正当なファイルか）
            var isKnownNodeFile = _scriptNodeId.Values.Contains(filePath, StringComparer.OrdinalIgnoreCase);

            if (isKnownNodeFile || !hasDependents)
            {
                // 通常の単体ノードファイルとして再コンパイル（自身の.srclist内容は自動的に同梱される）
                await CompileScriptFileAsync(filePath, isHotReload: true);
            }

            if (hasDependents)
            {
                var dependents = deps!.Keys.Where(d => !string.Equals(d, filePath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (dependents.Count > 0)
                {
                    _log.LogInfo($"[Scripts] Shared file changed: {Path.GetFileName(filePath)} — recompiling {dependents.Count} dependent node(s)");
                    foreach (var dep in dependents)
                    {
                        if (File.Exists(dep))
                            await CompileScriptFileAsync(dep, isHotReload: true);
                    }
                }
            }
        }
        catch (Exception ex) { _log.LogError($"[Scripts] Hot-reload error ({Path.GetFileName(filePath)}): {ex.Message}"); }
    }

    /// <summary>
    /// ノード.csファイルを、対応する.srclist（追加ソース）・.rsp（コンパイラオプション）込みで
    /// コンパイル・登録する。起動時ロードとホットリロードの両方から共通で使う。
    /// </summary>
    private async Task<bool> CompileScriptFileAsync(string filePath, bool isHotReload)
    {
        if (_nodeRegistry == null) return false;
        if (!File.Exists(filePath)) return false;

#if NET6_0_OR_GREATER
        var source = await File.ReadAllTextAsync(filePath);
#else
        var source = await Task.Run(() => File.ReadAllText(filePath));
#endif
        var className = Path.GetFileNameWithoutExtension(filePath);

        List<(string Source, string FileName)>? extraSources = null;
        if (_srclistResolved.TryGetValue(filePath, out var resolvedPaths) && resolvedPaths.Count > 0)
        {
            extraSources = new List<(string, string)>();
            foreach (var extraPath in resolvedPaths)
            {
                if (string.Equals(extraPath, filePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(extraPath))
                {
                    _log.LogWarning($"[Scripts] srclist entry missing at compile time: {extraPath} (for {Path.GetFileName(filePath)})");
                    continue;
                }
                try
                {
#if NET6_0_OR_GREATER
                    var extraSrc = await File.ReadAllTextAsync(extraPath);
#else
                    var extraSrc = await Task.Run(() => File.ReadAllText(extraPath));
#endif
                    extraSources.Add((extraSrc, Path.GetFileName(extraPath)));
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"[Scripts] Failed to read srclist entry {extraPath}: {ex.Message}");
                }
            }
        }

        var rspPath = Path.ChangeExtension(filePath, ".rsp");
        if (!File.Exists(rspPath)) rspPath = null;

        var response = await RoslynCompiler.CompileAndRegisterAsync(
            source, className, _nodeRegistry, _log, persist: false, dynamicNodesDir: null,
            extraSources: extraSources, rspFilePath: rspPath);

        if (response.Success)
        {
            var staleKeys = _scriptNodeId.Where(kv => kv.Value == filePath).Select(kv => kv.Key).ToList();
            foreach (var k in staleKeys) _scriptNodeId.TryRemove(k, out _);
            foreach (var nid in response.NodeIds)
            {
                if (_scriptNodeId.TryGetValue(nid, out var existingFile) && existingFile != filePath)
                {
                    var verb = isHotReload ? "Hot-reloaded" : "Registered";
                    var warn = $"[Scripts] Duplicate node ID detected: '{nid}' — also defined in '{Path.GetFileName(existingFile)}'. {verb} '{Path.GetFileName(filePath)}' will override.";
                    _log.LogWarning(warn);
                    _graphServer?.BroadcastWarningLog(warn);
                }
                _scriptNodeId[nid] = filePath;
            }
            _log.LogInfo($"[Scripts] {(isHotReload ? "Hot-reloaded" : "Registered")}: {string.Join(",", response.NodeIds)} ({Path.GetFileName(filePath)})");
            if (isHotReload) _graphServer?.BroadcastNodeListUpdated(response.NodeId);
            return true;
        }
        else
        {
            var verb = isHotReload ? "Hot-reload failed" : "Registration failed";
            _log.LogError($"[Scripts] {verb}: {Path.GetFileName(filePath)} — {response.ErrorMessage}");
            _graphServer?.BroadcastScriptCompileError(Path.GetFileName(filePath), response.ErrorMessage ?? "Compilation failed", response.Diagnostics);
            return false;
        }
    }

    // ---- .srclist インデックス ----

    private static string GetNodeCsPathForSrclist(string srclistPath) => Path.ChangeExtension(srclistPath, ".cs");

    /// <summary>
    /// .srclist を読み取り、相対パス（ディレクトリ指定は末尾 / で一括展開）を解決する。
    /// 対応するノード.cs自身は明示的に書かれていなくても常に結果セットへ含める。
    /// </summary>
    private List<string> ResolveSrclist(string srclistPath) =>
        RoslynCompiler.ResolveSrclist(srclistPath, _log);

    /// <summary>
    /// .srclist の内容から _srclistResolved（順引き）・_sharedFileDependents（逆引き）を
    /// 再構築する。既存エントリがあれば古い逆引きを先に除去してから新しい内容で登録し直す。
    /// </summary>
    private void RebuildSrclistIndex(string srclistPath)
    {
        var nodeCsPath = GetNodeCsPathForSrclist(srclistPath);
        RemoveReverseIndexFor(nodeCsPath);

        var resolved = ResolveSrclist(srclistPath);
        _srclistResolved[nodeCsPath] = resolved;

        foreach (var path in resolved)
        {
            if (string.Equals(path, nodeCsPath, StringComparison.OrdinalIgnoreCase)) continue; // 自己参照はスキップ
            var deps = _sharedFileDependents.GetOrAdd(path, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
            deps[nodeCsPath] = 1;
        }
    }

    private void RemoveSrclistIndex(string srclistPath)
    {
        var nodeCsPath = GetNodeCsPathForSrclist(srclistPath);
        RemoveReverseIndexFor(nodeCsPath);
        _srclistResolved.TryRemove(nodeCsPath, out _);
    }

    private void RemoveReverseIndexFor(string nodeCsPath)
    {
        if (!_srclistResolved.TryGetValue(nodeCsPath, out var oldResolved)) return;
        foreach (var old in oldResolved)
        {
            if (_sharedFileDependents.TryGetValue(old, out var deps))
            {
                deps.TryRemove(nodeCsPath, out _);
                if (deps.IsEmpty) _sharedFileDependents.TryRemove(old, out _);
            }
        }
    }

    private async Task LoadCustomScriptsAsync(string scriptsDir)
    {
        if (_nodeRegistry == null) return;
        try
        {
            // 1. 全 .srclist を先に読み込みインデックスを構築（順引き・逆引き）
            var srclistFiles = Directory.GetFiles(scriptsDir, "*.srclist", SearchOption.AllDirectories);
            foreach (var srclistPath in srclistFiles)
            {
                try { RebuildSrclistIndex(srclistPath); }
                catch (Exception ex) { _log.LogWarning($"[Scripts] Failed to index srclist {srclistPath}: {ex.Message}"); }
            }
            if (srclistFiles.Length > 0)
                _log.LogInfo($"[Scripts] {srclistFiles.Length} .srclist file(s) indexed");

            var csFiles = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);
            if (csFiles.Length == 0) return;

            _log.LogInfo($"[Scripts] {csFiles.Length} .cs file(s) found in {scriptsDir}");
            foreach (var file in csFiles)
            {
                try
                {
                    if (await ShouldSkipStandaloneCompileAsync(file))
                    {
                        _log.LogDebug($"[Scripts] Skipping non-node file (no [NodeType] / shared via .srclist): {Path.GetFileName(file)}");
                        continue;
                    }
                    await CompileScriptFileAsync(file, isHotReload: false);
                }
                catch (Exception ex) { _log.LogError($"[Scripts] Error loading {Path.GetFileName(file)}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { _log.LogError($"[Scripts] LoadCustomScripts error: {ex.Message}"); }
    }

    /// <summary>
    /// 起動時スキャンで、この .cs を単体ノードとしてコンパイルする必要が無いかを判定する。
    /// ファイル読み取り部分のみを担い、実際の判定は <see cref="ShouldSkipStandaloneCompile"/> に委譲する。
    /// </summary>
    private async Task<bool> ShouldSkipStandaloneCompileAsync(string filePath)
    {
        var hasDependents = _sharedFileDependents.ContainsKey(filePath);
#if NET6_0_OR_GREATER
        var source = await File.ReadAllTextAsync(filePath);
#else
        var source = await Task.Run(() => File.ReadAllText(filePath));
#endif
        return ShouldSkipStandaloneCompile(source, hasDependents);
    }

    /// <summary>
    /// この .cs のソースを単体ノードとしてコンパイルする必要が無いかを判定する（純粋関数、単体テスト用に internal 公開）。
    /// 以下のいずれかに該当すればスキップ対象（OR条件）:
    /// 1. ソースに [NodeType] 属性が存在しない（.srclist 登録の有無に関係なくベース判定）
    /// 2. 何らかの .srclist の対象ファイル集合に含まれる（hasDependents＝他ノードの .srclist に
    ///    明示的に登録済み。[NodeType] を持つ場合の保険的な追加条件）
    /// </summary>
    internal static bool ShouldSkipStandaloneCompile(string source, bool hasDependents)
        => hasDependents || !source.Contains("[NodeType");

    private void RunStartupAutoExecution()
    {
        try
        {
            var graphId = NgolConfig.StartupGraphId;
            var nodeTypeId = NgolConfig.StartupNodeTypeId;

            if (string.IsNullOrWhiteSpace(graphId) && string.IsNullOrWhiteSpace(nodeTypeId))
                return;

            if (!string.IsNullOrWhiteSpace(graphId))
            {
                if (!string.IsNullOrWhiteSpace(nodeTypeId))
                    _log.LogWarning("[Startup] Both startupGraphId and startupNodeTypeId are set; using startupGraphId.");

                var id = graphId.Trim();
                _log.LogInfo($"[Startup] Auto-executing graph: {id}");

                if (_graphSaveDir == null || _graphServer == null)
                {
                    _log.LogWarning("[Startup] Runtime not ready; skipping auto-execution.");
                    return;
                }

                var graph = GraphPersistenceHelper.TryLoad(id, _graphSaveDir);
                if (graph == null)
                {
                    _log.LogWarning($"[Startup] Graph not found: {id}");
                    return;
                }

                _graphServer.EnqueueStartupExecution(graph);
                return;
            }

            var typeId = nodeTypeId.Trim();
            _log.LogInfo($"[Startup] Auto-executing node: {typeId}");

            if (_graphServer == null)
            {
                _log.LogWarning("[Startup] Runtime not ready; skipping auto-execution.");
                return;
            }

            JsonElement inputs = default;
            var inputsJson = NgolConfig.StartupNodeInputsJson;
            if (!string.IsNullOrWhiteSpace(inputsJson))
            {
                using var doc = JsonDocument.Parse(inputsJson);
                inputs = doc.RootElement.Clone();
            }

            _graphServer.RunStartupNode(typeId, inputs);
        }
        catch (Exception ex)
        {
            _log.LogError($"[Startup] Auto-execution failed: {ex.Message}");
        }
    }

    // ---- 初期化ヘルパー ----

    private IKVStore CreateKVStore(string pluginDir)
    {
        var dbPath = Path.Combine(pluginDir, "kvstore.db");
        IKVStoreBackend backend;
        try
        {
            backend = new LiteDBBackend(dbPath);
            _log.LogInfo($"[KVStore] LiteDB initialized: {dbPath}");
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[KVStore] LiteDB init failed ({ex.Message}), falling back to JSON");
            backend = new JsonFileBackend(Path.ChangeExtension(dbPath, ".json"));
        }
        return new KVStore(backend);
    }

    private void PreloadRoslynAssemblies(string pluginDir)
    {
        string[] roslynDlls = ["Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll"];
        foreach (var dll in roslynDlls)
        {
            var path = Path.Combine(pluginDir, dll);
            if (File.Exists(path))
            {
                try { Assembly.LoadFrom(path); }
                catch (Exception ex) { _log.LogWarning($"[Runtime] Preload failed: {dll} — {ex.Message}"); }
            }
        }

        // Roslyn でコンパイルされた動的アセンブリがホスト固有の型を解決できるよう
        // AppDomain.AssemblyResolve に ALC ブリッジを登録する。
        // DefaultLoadContext にない参照でも他 ALC（ホスト側がロード済み）のアセンブリを返す。
        RegisterAlcBridgeResolver(pluginDir);
    }

    private static bool _alcBridgeRegistered;

    private void RegisterAlcBridgeResolver(string pluginDir)
    {
        if (_alcBridgeRegistered) return;
        _alcBridgeRegistered = true;

        // extra-libs はホストが動的コンパイルノードに追加で参照させたいDLLを置く規約フォルダ
        // （pluginDir/../../extra-libs、RoslynCompiler.BuildReferencePaths と同じ規約）
        var extraLibsDir = Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "extra-libs"));
        if (!Directory.Exists(extraLibsDir)) extraLibsDir = null;

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var shortName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(shortName)) return null;
            try
            {
                // 1. すでにロード済みのアセンブリから探す
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == shortName);
                if (existing != null) return existing;

                // 2. extra-libs フォルダから読み込む（ホスト固有型の直接参照用）
                if (extraLibsDir != null)
                {
                    var path = Path.Combine(extraLibsDir, shortName + ".dll");
                    if (File.Exists(path))
                        return Assembly.LoadFrom(path);
                }
            }
            catch { }
            return null;
        };

        _log.LogInfo($"[Runtime] ALC bridge resolver registered (extra-libs={extraLibsDir ?? "none"})");
    }

    private static bool IsNotUnderExtensionsFolder(string dllPath)
    {
        var normalized = dllPath.Replace('/', Path.DirectorySeparatorChar);
        var marker = Path.DirectorySeparatorChar + "Extensions" + Path.DirectorySeparatorChar;
        return normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var watchers in _watchersByDir.Values)
            foreach (var w in watchers) { try { w.Dispose(); } catch { } }
        _watchersByDir.Clear();
        try { _runner?.ClearAll(); } catch { }
        try { _extensionHost?.UnloadAll(); } catch { }
        try { _graphServer?.Dispose(); } catch { }
        try { (_store as IDisposable)?.Dispose(); } catch { }
    }
}
