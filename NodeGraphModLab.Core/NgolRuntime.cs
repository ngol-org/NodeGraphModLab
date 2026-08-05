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
    private readonly Server.HotReloadGate _hotReloadGate = new();

    // .srclist で解決済みの追加ソースファイル一覧（ノード.csパス→解決済み絶対パス一覧、自身を含む）
    private readonly ConcurrentDictionary<string, List<string>> _srclistResolved = new(StringComparer.OrdinalIgnoreCase);
    // 共有ファイルパス → それを参照している依存ノード.csパス一覧（逆引き）
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _sharedFileDependents = new(StringComparer.OrdinalIgnoreCase);

    // 監視対象ディレクトリ（プライマリ + 追加ディレクトリ）。ログのパス表示をディレクトリ相対にするために保持する。
    // Initialize() 内で一度だけ代入し、以降は読み取り専用。
    private IReadOnlyList<string> _scanDirs = Array.Empty<string>();

    public IKVStore? Store => _store;

    /// <summary>ホスト初期化時、GCワークアラウンドの要否を判定した後に呼ぶ。</summary>
    public void SetGcWorkaround(bool enable) => _needsGcWorkaround = enable;

    public NgolRuntime(INgolLogger log, NgolRuntimeOptions? options = null)
    {
        _log = log;
        _options = options ?? new NgolRuntimeOptions();
        _hotReloadGate.SetPendingCountProvider(() => _pendingRecompile.Count);
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
        var scanDirs = new List<string> { nodesDir };
        scanDirs.AddRange(extraNodeDirs);
        // ログのパス表示に使う。読み手（Task.Run のスキャン処理・ファイル監視のコールバック）は
        // いずれもこの代入より後に起動されるため、代入が見えることは保証される。
        _scanDirs = scanDirs;

        RoslynCompiler.LoadPersistedNodes(dynamicNodesDir, _nodeRegistry, _log);

        // 読み込みタスクは起動時自動実行をサーバー起動完了後に発火させる必要があるため、
        // TaskCompletionSource で待ち合わせる。初期化が例外で中断した場合は finally で false を渡し、
        // タスクが永久待機しないようにする。
        var serverReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => LoadCustomScriptsAndRunStartupAsync(scanDirs, serverReady.Task));

        StartScriptsWatcher(nodesDir, isPrimary: true);
        foreach (var dir in extraNodeDirs) StartScriptsWatcher(dir, isPrimary: false);

        try
        {
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
                _options.RuntimeType,
                _hotReloadGate);
            _graphServer.Start();
            serverReady.TrySetResult(true);

            _log.LogInfo("[NgolRuntime] initialized");

            if (_options.EnableDirectMode)
            {
                _drainThread = new Thread(DrainLoop) { IsBackground = true, Name = "NGOL-Drain" };
                _drainThread.Start();
            }
        }
        finally
        {
            serverReady.TrySetResult(false);
        }
    }

    /// <summary>
    /// 起動時のカスタムノード読み込み全体を、以下の順で進める。
    /// 1. プリスキャン（.srclist インデックス構築 + 全 .cs のノードタイプ ID 抽出）
    /// 2. 最優先指定ノード・起動時自動実行の対象ノードを先にコンパイル
    /// 3. サーバー起動完了を待って起動時自動実行を発火
    /// 4. 残りのノードを継続コンパイル（3 の実行と並行して進む）
    /// </summary>
    private async Task LoadCustomScriptsAndRunStartupAsync(IReadOnlyList<string> scanDirs, Task<bool> serverReady)
    {
        var compiledPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prescan = await PrescanCustomScriptsAsync(scanDirs);

        NodeGraph? startupGraph = null;
        try
        {
            startupGraph = TryLoadStartupGraph();
            var startupNodeTypeIds = CollectStartupNodeTypeIds(startupGraph);
            var plan = BuildPriorityCompileOrder(
                prescan.NodeTypeIdToPath,
                NgolConfig.PriorityCompileNodeTypeIds,
                startupNodeTypeIds,
                out var unresolved);

            foreach (var id in unresolved)
                _log.LogWarning($"[Startup] priorityCompileNodeTypeIds entry '{id}' has no matching custom node source; skipping priority compile.");

            if (plan.Count > 0)
            {
                _log.LogInfo($"[Startup] Priority-compiling {plan.Count} source file(s) before auto-execution");
                await CompileScriptFilesAsync(plan, compiledPaths);
            }
        }
        catch (Exception ex) { _log.LogError($"[Startup] Priority compile error: {ex.Message}"); }

        var ready = false;
        try { ready = await serverReady; }
        catch (Exception ex) { _log.LogError($"[Startup] Failed to wait for server startup: {ex.Message}"); }

        if (ready) RunStartupAutoExecution(startupGraph);
        else _log.LogWarning("[Startup] Runtime not ready; skipping auto-execution.");

        foreach (var dir in scanDirs)
        {
            if (prescan.CompileTargetsByDir.TryGetValue(dir, out var targets))
                await CompileScriptFilesAsync(targets, compiledPaths);
        }
    }

    // ---- MonoBehaviour ライフサイクル ----

    public void Tick()
    {
        if (!_hotReloadGate.IsPaused)
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

                if (!_hotReloadGate.IsPaused)
                {
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

    /// <summary>
    /// .rsp/.srclist 削除時の復帰コンパイルなど、デバウンスを経由しない即時コンパイル要求の入り口。
    /// 一時停止中は _pendingRecompile に載せて既存のデバウンスドレインへ合流させ、再開時に処理する。
    /// </summary>
    private void TriggerCompile(string nodeCsPath)
    {
        if (_hotReloadGate.IsPaused)
        {
            _pendingRecompile[nodeCsPath] = DateTime.Now;
            return;
        }
        _ = Task.Run(async () => await CompileScriptFileAsync(nodeCsPath, isHotReload: true));
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
                TriggerCompile(nodeCsPath);
            return;
        }

        if (string.Equals(ext, ".rsp", StringComparison.OrdinalIgnoreCase))
        {
            var nodeCsPath = Path.ChangeExtension(e.FullPath, ".cs");
            _log.LogInfo($"[Scripts] rsp deleted: {Path.GetFileName(e.FullPath)} — reverting to default compile options");
            if (File.Exists(nodeCsPath))
                TriggerCompile(nodeCsPath);
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
                    var warn = $"[Scripts] Duplicate node ID detected: '{nid}' — also defined in '{FormatScriptPathForLog(existingFile, _scanDirs)}'. {verb} '{FormatScriptPathForLog(filePath, _scanDirs)}' will override.";
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

    /// <summary>
    /// ログ表示用にスクリプトのパスを整形する（純粋関数）。
    /// 先頭の監視ディレクトリ配下なら、そこからの相対パスを '/' 区切りで返す。
    /// それ以外（追加の監視ディレクトリ配下・監視対象外）はフルパスを返す。
    /// 相対化を先頭ディレクトリのみに限るのは、別ディレクトリ間で相対パスが衝突して
    /// どちらのファイルか取り違えるのを避けるため。
    /// </summary>
    internal static string FormatScriptPathForLog(string fullPath, IReadOnlyList<string>? scanDirs)
    {
        if (string.IsNullOrEmpty(fullPath)) return fullPath;
        if (scanDirs == null || scanDirs.Count == 0) return fullPath;

        var root = scanDirs[0];
        if (string.IsNullOrEmpty(root)) return fullPath;
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 単純な前方一致だと "<root>2\A.cs" のような別ディレクトリまで相対化してしまうため、
        // root の直後が区切り文字であることまで確認する。
        if (fullPath.Length <= root.Length + 1) return fullPath;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return fullPath;

        var sep = fullPath[root.Length];
        if (sep != Path.DirectorySeparatorChar && sep != Path.AltDirectorySeparatorChar) return fullPath;

        return fullPath.Substring(root.Length + 1).Replace('\\', '/');
    }

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

    /// <summary>起動時のカスタムノード .cs 事前スキャン結果。</summary>
    private sealed class ScriptPrescanResult
    {
        /// <summary>監視ディレクトリごとの、単体コンパイル対象 .cs パス一覧（ディレクトリ走査順）。</summary>
        public Dictionary<string, List<string>> CompileTargetsByDir { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>ノードタイプ ID → それを宣言している .cs パス。</summary>
        public Dictionary<string, string> NodeTypeIdToPath { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// 監視対象ディレクトリ配下の .cs を 1 回ずつ読み、単体コンパイル対象の判定と
    /// ノードタイプ ID の抽出をまとめて行う。ID → ファイルの対応が分かることで、
    /// 起動時自動実行に必要なノードだけを先にコンパイルできる。
    /// </summary>
    private async Task<ScriptPrescanResult> PrescanCustomScriptsAsync(IReadOnlyList<string> scanDirs)
    {
        var result = new ScriptPrescanResult();
        if (_nodeRegistry == null) return result;

        // .srclist は全ディレクトリ分を先に索引化する。共有ファイルと、それを参照するノードが
        // 別ディレクトリにある構成でも hasDependents 判定が正しくなる。
        foreach (var scriptsDir in scanDirs)
        {
            try
            {
                var srclistFiles = Directory.GetFiles(scriptsDir, "*.srclist", SearchOption.AllDirectories);
                foreach (var srclistPath in srclistFiles)
                {
                    try { RebuildSrclistIndex(srclistPath); }
                    catch (Exception ex) { _log.LogWarning($"[Scripts] Failed to index srclist {srclistPath}: {ex.Message}"); }
                }
                if (srclistFiles.Length > 0)
                    _log.LogInfo($"[Scripts] {srclistFiles.Length} .srclist file(s) indexed in {scriptsDir}");
            }
            catch (Exception ex) { _log.LogError($"[Scripts] srclist scan error in {scriptsDir}: {ex.Message}"); }
        }

        foreach (var scriptsDir in scanDirs)
        {
            var targets = new List<string>();
            result.CompileTargetsByDir[scriptsDir] = targets;

            try
            {
                var csFiles = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);
                if (csFiles.Length == 0) continue;

                _log.LogInfo($"[Scripts] {csFiles.Length} .cs file(s) found in {scriptsDir}");

                foreach (var file in csFiles)
                {
                    try
                    {
#if NET6_0_OR_GREATER
                        var source = await File.ReadAllTextAsync(file);
#else
                        var source = await Task.Run(() => File.ReadAllText(file));
#endif
                        if (ShouldSkipStandaloneCompile(source, _sharedFileDependents.ContainsKey(file)))
                        {
                            _log.LogDebug($"[Scripts] Skipping non-node file (no [NodeType] / shared via .srclist): {Path.GetFileName(file)}");
                            continue;
                        }

                        targets.Add(file);

                        foreach (var id in ExtractNodeTypeIds(source))
                        {
                            if (result.NodeTypeIdToPath.TryGetValue(id, out var existing))
                            {
                                _log.LogWarning($"[Scripts] Node type ID '{id}' is declared in both '{FormatScriptPathForLog(existing, _scanDirs)}' and '{FormatScriptPathForLog(file, _scanDirs)}'; priority compile will use the former.");
                                continue;
                            }
                            result.NodeTypeIdToPath[id] = file;
                        }
                    }
                    catch (Exception ex) { _log.LogError($"[Scripts] Prescan error for {Path.GetFileName(file)}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { _log.LogError($"[Scripts] Prescan error in {scriptsDir}: {ex.Message}"); }
        }

        return result;
    }

    /// <summary>
    /// 指定した .cs 群を順にコンパイル・登録する。<paramref name="compiledPaths"/> に
    /// 記録済みのパスはスキップするため、優先コンパイル済みのファイルが再コンパイルされない。
    /// </summary>
    private async Task CompileScriptFilesAsync(IReadOnlyList<string> filePaths, HashSet<string> compiledPaths)
    {
        foreach (var file in filePaths)
        {
            if (!compiledPaths.Add(file)) continue;
            try { await CompileScriptFileAsync(file, isHotReload: false); }
            catch (Exception ex) { _log.LogError($"[Scripts] Error loading {Path.GetFileName(file)}: {ex.Message}"); }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex NodeTypeIdPattern =
        new(@"\[\s*NodeType(?:Attribute)?\s*\(\s*""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// ソース中の [NodeType("...")] からノードタイプ ID を宣言順に抽出する（純粋関数、単体テスト用に internal 公開）。
    /// ID を文字列リテラル以外（定数参照等）で指定している場合は抽出できないが、その場合は
    /// 優先コンパイルの対象にならないだけで、通常のコンパイルパスで登録される。
    /// </summary>
    internal static List<string> ExtractNodeTypeIds(string source)
    {
        var ids = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in NodeTypeIdPattern.Matches(source))
        {
            var id = match.Groups[1].Value.Trim();
            if (id.Length == 0) continue;
            if (ids.Contains(id, StringComparer.Ordinal)) continue;
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// 起動時自動実行より前にコンパイルすべき .cs パスを順序付きで返す（純粋関数、単体テスト用に internal 公開）。
    /// 設定で明示指定されたノード（記載順）→ 起動時自動実行の対象ノード、の順に並べ、同一ファイルの重複は除去する。
    /// 起動対象 ID に対応する .cs が無い場合は、ビルトイン等の既に登録済みのノードなので黙って無視する。
    /// </summary>
    internal static List<string> BuildPriorityCompileOrder(
        IReadOnlyDictionary<string, string> nodeTypeIdToPath,
        IReadOnlyList<string> priorityNodeTypeIds,
        IReadOnlyList<string> startupNodeTypeIds,
        out List<string> unresolvedPriorityNodeTypeIds)
    {
        var order = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        unresolvedPriorityNodeTypeIds = new List<string>();

        foreach (var rawId in priorityNodeTypeIds)
        {
            if (string.IsNullOrWhiteSpace(rawId)) continue;
            var id = rawId.Trim();
            if (!nodeTypeIdToPath.TryGetValue(id, out var path))
            {
                unresolvedPriorityNodeTypeIds.Add(id);
                continue;
            }
            if (seenPaths.Add(path)) order.Add(path);
        }

        foreach (var rawId in startupNodeTypeIds)
        {
            if (string.IsNullOrWhiteSpace(rawId)) continue;
            if (!nodeTypeIdToPath.TryGetValue(rawId.Trim(), out var path)) continue;
            if (seenPaths.Add(path)) order.Add(path);
        }

        return order;
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

    /// <summary>
    /// 起動時自動実行の対象グラフを読み込む。startupGraphId が未設定、または
    /// グラフが見つからない場合は null を返す（警告は実行時に <see cref="RunStartupAutoExecution"/> が出す）。
    /// </summary>
    private NodeGraph? TryLoadStartupGraph()
    {
        var graphId = NgolConfig.StartupGraphId;
        if (string.IsNullOrWhiteSpace(graphId) || _graphSaveDir == null) return null;
        return GraphPersistenceHelper.TryLoad(graphId.Trim(), _graphSaveDir);
    }

    /// <summary>
    /// 起動時自動実行が必要とするノードタイプ ID を重複なく列挙する。
    /// startupGraphId と startupNodeTypeId の優先順位は <see cref="RunStartupAutoExecution"/> と揃える。
    /// </summary>
    private static List<string> CollectStartupNodeTypeIds(NodeGraph? startupGraph)
    {
        var ids = new List<string>();

        if (!string.IsNullOrWhiteSpace(NgolConfig.StartupGraphId))
        {
            if (startupGraph == null) return ids;
            foreach (var node in startupGraph.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.NodeTypeId)) continue;
                if (ids.Contains(node.NodeTypeId, StringComparer.Ordinal)) continue;
                ids.Add(node.NodeTypeId);
            }
            return ids;
        }

        var nodeTypeId = NgolConfig.StartupNodeTypeId;
        if (!string.IsNullOrWhiteSpace(nodeTypeId)) ids.Add(nodeTypeId.Trim());
        return ids;
    }

    private void RunStartupAutoExecution(NodeGraph? preloadedStartupGraph)
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

                var graph = preloadedStartupGraph ?? GraphPersistenceHelper.TryLoad(id, _graphSaveDir);
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
        // 実装の選択と失敗時の退避はファクトリ側に閉じている。
        // ここで各実装の型に直接触れないことが重要で、触れるとその実装が依存する
        // アセンブリが、選ばれていなくてもこのメソッドの JIT 時に解決されてしまう。
        var backend = KVStoreBackendFactory.Create(NgolConfig.KvStoreBackend, pluginDir, _log, out var resolvedName);

        // 保存先を切り替えた直後の一度だけ、以前の保存先から中身を引き継ぐ。
        // KVStore がメモリへ読み込む前に済ませる必要があるため、ここで行う。
        KVStoreBackendFactory.MigrateIfRequested(NgolConfig.KvStoreMigrateFrom, resolvedName, backend, pluginDir, _log);

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
        // 終了後は DrainUpdate の周回を当てにできないため、OnStop はここで同期発火させる。
        try { _runner?.ClearAllImmediate(); } catch { }
        try { _extensionHost?.UnloadAll(); } catch { }
        try { _graphServer?.Dispose(); } catch { }
        try { (_store as IDisposable)?.Dispose(); } catch { }
    }
}
