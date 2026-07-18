using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

/// <summary>
/// ブラウザから受け取った C# ソースコードを Roslyn でコンパイルし、
/// NodeRegistry に動的登録する。
/// persist=true の場合は dynamic-nodes/ ディレクトリに DLL を保存する。
/// </summary>
public static class RoslynCompiler
{
    // ホスト .NET バージョンに対応するプリプロセッサシンボルをスクリプトにも伝播させる。
    // これにより cs ホットリロードファイル内で #if NET6_0_OR_GREATER が正しく動作する。
#if NET6_0_OR_GREATER
    private static readonly string[] ScriptPreprocessorSymbols = ["NET6_0_OR_GREATER"];
#else
    private static readonly string[] ScriptPreprocessorSymbols = [];
#endif
    private static readonly CSharpParseOptions ScriptParseOptions =
        CSharpParseOptions.Default.WithPreprocessorSymbols(ScriptPreprocessorSymbols);

    // 一部 IL2CPP 環境では System.Runtime.dll に NullableContextAttribute が存在しない場合がある。
    // CS0656 を回避するため、コンパイル時にポリフィルを注入する。
    private static readonly SyntaxTree NullablePolyfillTree = CSharpSyntaxTree.ParseText(@"
namespace System.Runtime.CompilerServices {
    [AttributeUsage(AttributeTargets.Module|AttributeTargets.Class|AttributeTargets.Struct|AttributeTargets.Method|AttributeTargets.Interface|AttributeTargets.Delegate,AllowMultiple=false,Inherited=false)]
    internal sealed class NullableContextAttribute : Attribute { public NullableContextAttribute(byte v){} }
    [AttributeUsage(AttributeTargets.Module|AttributeTargets.Class|AttributeTargets.Struct|AttributeTargets.Method|AttributeTargets.Interface|AttributeTargets.Delegate|AttributeTargets.Field|AttributeTargets.Parameter|AttributeTargets.ReturnValue|AttributeTargets.GenericParameter|AttributeTargets.Property|AttributeTargets.Event,AllowMultiple=false,Inherited=false)]
    internal sealed class NullableAttribute : Attribute { public NullableAttribute(byte v){} public NullableAttribute(byte[] v){} }
}");

    // IL2CPP 環境で必要な参照アセンブリのファイル名
    private static readonly string[] RequiredReferenceNames =
    {
        "System.Runtime.dll",
        "System.Collections.dll",
        "System.Linq.dll",
        "System.Text.Json.dll",
        "netstandard.dll",
    };

    /// <summary>
    /// C# ソースをコンパイルし、成功した場合は NodeRegistry に登録する。
    /// </summary>
    /// <param name="persist">true の場合は DLL をファイルシステムに保存する</param>
    /// <param name="dynamicNodesDir">DLL 保存先ディレクトリ（persist=true の場合に使用）</param>
    public static async Task<CompileNodeResponse> CompileAndRegisterAsync(
        string source,
        string className,
        NodeRegistry registry,
        INgolLogger log,
        bool persist = false,
        string? dynamicNodesDir = null,
        IReadOnlyList<(string Source, string FileName)>? extraSources = null,
        string? rspFilePath = null)
    {
        return await Task.Run(() =>
            CompileAndRegister(source, className, registry, log, persist, dynamicNodesDir, extraSources, rspFilePath));
    }

    private static CompileNodeResponse CompileAndRegister(
        string source,
        string className,
        NodeRegistry registry,
        INgolLogger log,
        bool persist,
        string? dynamicNodesDir,
        IReadOnlyList<(string Source, string FileName)>? extraSources = null,
        string? rspFilePath = null)
    {
        // 0. .rsp（コンパイラオプション）を解決
        var rspDiagnostics = new List<string>();
        CSharpParseOptions? rspParseOptions = null;
        CSharpCompilationOptions? rspCompilationOptions = null;
        var rspReferences = new List<MetadataReference>();
        var rspAnalyzerPaths = new List<string>();
        if (!string.IsNullOrEmpty(rspFilePath))
        {
            var (po, co, refs, analyzerPaths, errs) = ParseRspFile(rspFilePath!, className + ".cs", log);
            rspParseOptions = po;
            rspCompilationOptions = co;
            rspAnalyzerPaths = analyzerPaths;
            rspReferences = refs;
            rspDiagnostics = errs;
        }

        var effectiveParseOptions = rspParseOptions != null
            ? rspParseOptions.WithPreprocessorSymbols(
                rspParseOptions.PreprocessorSymbolNames.Union(ScriptPreprocessorSymbols).Distinct())
            : ScriptParseOptions;

        var effectiveCompilationOptions = rspCompilationOptions != null
            ? rspCompilationOptions.WithOutputKind(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true)
            : new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);

        // 1. 構文木を生成（.srclist で解決された追加ソースがあれば同梱）
        var syntaxTree = CSharpSyntaxTree.ParseText(source, effectiveParseOptions, path: className + ".cs");
        var extraTrees = new List<SyntaxTree>();
        if (extraSources != null)
        {
            foreach (var (extraSource, fileName) in extraSources)
                extraTrees.Add(CSharpSyntaxTree.ParseText(extraSource, effectiveParseOptions, path: fileName));
        }

        // アセンブリ名（永続化の場合は className ベース）
        var asmBaseName = persist && !string.IsNullOrEmpty(className)
            ? SanitizeFileName(className)
            : $"DynamicNode_{Guid.NewGuid():N}";

        // BuildReferencePaths は Roslyn 型を一切持たない純粋パス収集メソッド。
        // <>O 重複型を持つ interop DLL は MetadataReference 化の時点でスキップし、
        // skipAllExtraLibs フォールバックでホスト提供の追加参照アセンブリを全除外できるようにする。
        bool skipAllExtraLibs = false;
        // CS1069「型が別アセンブリに転送された」エラーで自動追加した参照パス
        var extraRefPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MemoryStream ms = new();
        EmitResult emitResult = default!;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                log.LogDebug($"[RoslynCompiler] Compile attempt {attempt} starting (skipAllExtraLibs={skipAllExtraLibs})");
                var paths = BuildReferencePaths(log, skipAllExtraLibs);
                log.LogDebug($"[RoslynCompiler] Collected {paths.Count} reference assembly path(s)");

                var references = new List<MetadataReference>();
                foreach (var path in paths)
                {
                    try { references.Add(MetadataReference.CreateFromFile(path)); }
                    catch (Exception ex) { log.LogDebug($"[RoslynCompiler] Skipping bad ref: {Path.GetFileName(path)} — {ex.Message}"); }
                }
                foreach (var path in extraRefPaths)
                {
                    try { references.Add(MetadataReference.CreateFromFile(path)); }
                    catch { }
                }
                references.Add(MetadataReference.CreateFromFile(typeof(INode).Assembly.Location));
                references.AddRange(rspReferences);
                log.LogDebug($"[RoslynCompiler] Resolved {references.Count} MetadataReference(s) (includes NodeAPI + .rsp references)");

                var compilation = CSharpCompilation.Create(
                    assemblyName: asmBaseName,
                    syntaxTrees: new SyntaxTree[] { syntaxTree, NullablePolyfillTree }.Concat(extraTrees),
                    references: references,
                    options: effectiveCompilationOptions);

                if (rspAnalyzerPaths.Count > 0)
                {
                    var (genCompilation, genDiagnostics) = RunSourceGenerators(compilation, rspAnalyzerPaths, effectiveParseOptions, log);
                    compilation = genCompilation;
                    rspDiagnostics.AddRange(genDiagnostics);
                }

                ms = new MemoryStream();
                emitResult = compilation.Emit(ms);

                // CS1069: 型が別アセンブリに転送された → 不足参照を自動追加してリトライ
                if (!emitResult.Success)
                {
                    var added = TryAddForwardedReferences(emitResult.Diagnostics, extraRefPaths, log);
                    if (added) continue;
                }
                break;
            }
            catch (BadImageFormatException ex)
            {
                log.LogDebug($"[RoslynCompiler] BadImageFormatException attempt={attempt}: {ex.Message}");
                if (!skipAllExtraLibs)
                {
                    skipAllExtraLibs = true;
                    log.LogDebug("[RoslynCompiler] Retrying with skipAllExtraLibs=true");
                }
                else
                {
                    return new CompileNodeResponse { Success = false, ErrorMessage = $"Compile error: {ex.Message}" };
                }
            }
        }

        var diagnostics = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToList();
        diagnostics.InsertRange(0, rspDiagnostics);

        if (!emitResult.Success)
        {
            var errors = diagnostics.Where(d => d.Contains("error", StringComparison.OrdinalIgnoreCase)).ToList();
            log.LogWarning($"[RoslynCompiler] Compile failed: {string.Join("; ", errors)}");
            return new CompileNodeResponse { Success = false, ErrorMessage = "Compilation failed", Diagnostics = diagnostics };
        }

        // 5. DLL 永続化
        string? savedDllPath = null;
        if (persist && !string.IsNullOrEmpty(dynamicNodesDir))
        {
            try
            {
                Directory.CreateDirectory(dynamicNodesDir);
                savedDllPath = Path.Combine(dynamicNodesDir, $"{asmBaseName}.dll");
                // 既存ファイルを上書きするため一旦削除
                if (File.Exists(savedDllPath))
                    File.Delete(savedDllPath);
                File.WriteAllBytes(savedDllPath, ms.ToArray());
                log.LogInfo($"[RoslynCompiler] DLL saved: {savedDllPath}");
            }
            catch (Exception ex)
            {
                log.LogWarning($"[RoslynCompiler] DLL persist failed: {ex.Message}");
                savedDllPath = null;
            }
        }

        // 6. アセンブリをロードして NodeRegistry に登録
        ms.Seek(0, SeekOrigin.Begin);
        Assembly asm;
        try
        {
            asm = Assembly.Load(ms.ToArray());
        }
        catch (Exception ex)
        {
            log.LogError($"[RoslynCompiler] Assembly.Load failed: {ex.Message}");
            return new CompileNodeResponse { Success = false, ErrorMessage = $"Load failed: {ex.Message}" };
        }

        var registered = registry.ScanAssembly(asm);
        List<string> allNodeIds;
        string nodeId;
        if (registered.Count > 0)
        {
            // 新規登録: 全IDを収集
            allNodeIds = registered;
            nodeId = registered[0];
            foreach (var nid in registered)
                log.LogInfo($"[RoslynCompiler] Registered dynamic node: {nid}");
        }
        else
        {
            // 既存ノードの再登録（upsert）かどうかを確認
            IEnumerable<Type> asmTypes;
            try { asmTypes = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { asmTypes = ex.Types.Where(t => t != null)!; }

            allNodeIds = asmTypes
                .Where(t => t != null && !t.IsAbstract && !t.IsInterface && typeof(INode).IsAssignableFrom(t))
                .Select(t => t.GetCustomAttribute<NodeTypeAttribute>()?.Id)
                .Where(id => id != null)
                .ToList()!;

            if (allNodeIds.Count == 0)
            {
                return new CompileNodeResponse
                {
                    Success = false,
                    ErrorMessage = $"No [NodeType] class found in compiled assembly. Ensure '{className}' has [NodeType(..)] attribute and implements INode.",
                    Diagnostics = diagnostics
                };
            }
            nodeId = allNodeIds[0];
            foreach (var nid in allNodeIds)
                log.LogInfo($"[RoslynCompiler] Re-registered dynamic node: {nid}");
        }

        return new CompileNodeResponse
        {
            Success = true,
            NodeId = nodeId,
            NodeIds = allNodeIds,
            Persisted = savedDllPath != null,
            SavedDllPath = savedDllPath,
            Diagnostics = diagnostics
        };
    }

    /// <summary>
    /// dynamic-nodes/ ディレクトリにある保存済み DLL をスキャンして再登録する。
    /// 起動時に呼び出す。
    /// </summary>
    public static void LoadPersistedNodes(string dynamicNodesDir, NodeRegistry registry, INgolLogger log)
    {
        if (!Directory.Exists(dynamicNodesDir)) return;

        var dlls = Directory.GetFiles(dynamicNodesDir, "*.dll");
        foreach (var dll in dlls)
        {
            try
            {
                var bytes = File.ReadAllBytes(dll);
                var asm = Assembly.Load(bytes);
                var registered = registry.ScanAssembly(asm);
                if (registered.Count > 0)
                    log.LogInfo($"[RoslynCompiler] Loaded persisted nodes from {Path.GetFileName(dll)}: {string.Join(", ", registered)}");
                else
                    log.LogDebug($"[RoslynCompiler] No node types in {Path.GetFileName(dll)} (skipped)");
            }
            catch (Exception ex)
            {
                log.LogWarning($"[RoslynCompiler] Failed to load {Path.GetFileName(dll)}: {ex.Message}");
            }
        }
    }

    // Roslyn 型を一切使わない純粋パス収集メソッド。
    // MetadataReference を戻り値やキャプチャ変数に含めないことで、
    // このメソッドの JIT コンパイル時に Roslyn アセンブリのロードが発生せず、
    // <>O 重複型由来の BadImageFormatException を回避できる。
    private static List<string> BuildReferencePaths(INgolLogger log, bool skipAllExtraLibs = false)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!seen.Add(path)) return;
            paths.Add(path);
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name)) seenNames.Add(name);
        }

        log.LogDebug($"[RoslynCompiler] BuildReferencePaths: gathering reference assembly paths (skipAllExtraLibs={skipAllExtraLibs})");
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        log.LogDebug($"[RoslynCompiler] Using .NET runtime directory: {runtimeDir}");
        foreach (var name in RequiredReferenceNames)
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path)) Add(path);
            else log.LogDebug($"[RoslynCompiler] Reference not found (skipped): {path}");
        }

        if (!skipAllExtraLibs)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!string.IsNullOrEmpty(asm.Location) && File.Exists(asm.Location))
                        Add(asm.Location);
                }
                catch { }
            }
        }

        try
        {
            // extra-libs: ホストが動的コンパイルノードに追加で参照させたいDLLを置く規約フォルダ
            // （<pluginDir>/../../extra-libs）。ホスト固有の型（例: ゲームエンジンのランタイム型）が
            // AppDomain に未ロードの場合のフォールバック。
            var libDir = Path.Combine(
                Path.GetDirectoryName(typeof(RoslynCompiler).Assembly.Location)!,
                "..", "..", "extra-libs");
            if (!skipAllExtraLibs && Directory.Exists(libDir))
            {
                int added = 0;
                foreach (var dll in Directory.GetFiles(libDir, "*.dll"))
                {
                    try
                    {
                        var asmName = Path.GetFileNameWithoutExtension(dll);
                        if (string.IsNullOrEmpty(asmName) || seenNames.Contains(asmName)) continue;
                        Add(dll);
                        added++;
                    }
                    catch { }
                }
                log.LogDebug($"[RoslynCompiler] Added {added} fallback assemblies from {libDir}");
            }
        }
        catch { }

        return paths;
    }

    private static string? TryExtractDuplicateAssemblyName(string message)
    {
        const string marker = "in assembly ";
        var idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        while (start < message.Length && !char.IsLetterOrDigit(message[start]) && message[start] != '_')
            start++;
        if (start >= message.Length) return null;
        var end = message.IndexOf(',', start);
        return end < 0 ? message.Substring(start).Trim() : message.Substring(start, end - start).Trim();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>
    /// CS1069「型が別アセンブリに転送された」エラーから不足アセンブリを特定し、
    /// ランタイムディレクトリで見つかればパスを extraRefPaths に追加する。
    /// 1件以上追加できた場合は true を返してリトライを促す。
    /// </summary>
    private static bool TryAddForwardedReferences(
        System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics,
        HashSet<string> extraRefPaths,
        INgolLogger log)
    {
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        bool added = false;
        foreach (var d in diagnostics)
        {
            if (d.Id != "CS1069") continue;
            var asmName = ExtractForwardedAssemblyName(d.GetMessage());
            if (asmName == null) continue;
            var path = Path.Combine(runtimeDir, asmName + ".dll");
            if (File.Exists(path) && extraRefPaths.Add(path))
            {
                log.LogInfo($"[RoslynCompiler] Auto-added missing reference: {asmName}.dll");
                added = true;
            }
        }
        return added;
    }

    /// <summary>
    /// .srclist（追加ソースファイル一覧専用のサイドカーファイル）を読み取り、相対パス
    /// （ディレクトリ指定は末尾 / で直下のみ一括展開、末尾 /** で配下を再帰的に一括展開）を解決する。
    /// 対応するノード .cs 自身は明示的に書かれていなくても常に結果セットへ含める（絶対パス・大文字小文字無視でユニーク化）。
    /// NgolRuntime（ホットリロード）・ExportNodesHandler（DLLパック配布）の両方から共通で使う。
    /// </summary>
    public static List<string> ResolveSrclist(string srclistPath, INgolLogger? log = null)
    {
        var nodeCsPath = Path.ChangeExtension(srclistPath, ".cs");
        var dir = Path.GetDirectoryName(srclistPath)!;
        var resolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            var full = Path.GetFullPath(path);
            if (seen.Add(full)) resolved.Add(full);
        }

        Add(nodeCsPath); // 暗黙フォールバック: 自身は常に含む

        try
        {
            foreach (var rawLine in File.ReadAllLines(srclistPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.EndsWith("/**") || line.EndsWith("\\**"))
                {
                    var subDir = Path.GetFullPath(Path.Combine(dir, line.Substring(0, line.Length - 2)));
                    if (Directory.Exists(subDir))
                    {
                        foreach (var f in Directory.GetFiles(subDir, "*.cs", SearchOption.AllDirectories))
                            Add(f);
                    }
                    else
                    {
                        log?.LogWarning($"[Scripts] srclist directory not found: {subDir} (from {Path.GetFileName(srclistPath)})");
                    }
                    continue;
                }

                if (line.EndsWith("/") || line.EndsWith("\\"))
                {
                    var subDir = Path.GetFullPath(Path.Combine(dir, line));
                    if (Directory.Exists(subDir))
                    {
                        foreach (var f in Directory.GetFiles(subDir, "*.cs"))
                            Add(f);
                    }
                    else
                    {
                        log?.LogWarning($"[Scripts] srclist directory not found: {subDir} (from {Path.GetFileName(srclistPath)})");
                    }
                    continue;
                }

                var filePath = Path.GetFullPath(Path.Combine(dir, line));
                if (File.Exists(filePath))
                    Add(filePath);
                else
                    log?.LogWarning($"[Scripts] srclist entry not found: {filePath} (from {Path.GetFileName(srclistPath)})");
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[Scripts] Failed to read srclist {srclistPath}: {ex.Message}");
        }

        return resolved;
    }

    /// <summary>
    /// .rsp（csc互換のコンパイラオプション専用レスポンスファイル）を Roslyn 純正の
    /// CSharpCommandLineParser でパースする。ソースファイルの列挙はここでは扱わない
    /// （.srclist の役割）。/r: は MetadataReference、/define: は ParseOptions、
    /// /nowarn: 等は CompilationOptions、/analyzer: は Source Generator DLL パスに反映される。
    /// </summary>
    private static (CSharpParseOptions? ParseOptions, CSharpCompilationOptions? CompilationOptions, List<MetadataReference> References, List<string> AnalyzerPaths, List<string> Errors)
        ParseRspFile(string rspFilePath, string placeholderMainFileName, INgolLogger? log)
    {
        var errors = new List<string>();
        var references = new List<MetadataReference>();
        var analyzerPaths = new List<string>();

        if (!File.Exists(rspFilePath))
        {
            errors.Add($"[Rsp] Response file not found: {rspFilePath}");
            return (null, null, references, analyzerPaths, errors);
        }

        try
        {
            var baseDirectory = Path.GetDirectoryName(rspFilePath) ?? Directory.GetCurrentDirectory();
            var sdkDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            var parsedArgs = CSharpCommandLineParser.Default.Parse(
                new[] { $"@{rspFilePath}", placeholderMainFileName },
                baseDirectory,
                sdkDirectory);

            foreach (var err in parsedArgs.Errors)
                errors.Add($"[Rsp:{Path.GetFileName(rspFilePath)}] {err}");

            foreach (var cmdRef in parsedArgs.MetadataReferences)
            {
                var refPath = cmdRef.Reference;
                if (!Path.IsPathRooted(refPath))
                    refPath = Path.Combine(baseDirectory, refPath);
                try
                {
                    if (File.Exists(refPath))
                        references.Add(MetadataReference.CreateFromFile(refPath));
                    else
                        errors.Add($"[Rsp:{Path.GetFileName(rspFilePath)}] Reference not found: {refPath}");
                }
                catch (Exception ex)
                {
                    errors.Add($"[Rsp:{Path.GetFileName(rspFilePath)}] Failed to load reference '{refPath}': {ex.Message}");
                }
            }

            foreach (var cmdAnalyzer in parsedArgs.AnalyzerReferences)
            {
                var analyzerPath = cmdAnalyzer.FilePath;
                if (!Path.IsPathRooted(analyzerPath))
                    analyzerPath = Path.Combine(baseDirectory, analyzerPath);
                if (File.Exists(analyzerPath))
                    analyzerPaths.Add(analyzerPath);
                else
                    errors.Add($"[Rsp:{Path.GetFileName(rspFilePath)}] Analyzer not found: {analyzerPath}");
            }

            return (parsedArgs.ParseOptions, parsedArgs.CompilationOptions, references, analyzerPaths, errors);
        }
        catch (Exception ex)
        {
            log?.LogWarning($"[RoslynCompiler] Rsp parse failed ({rspFilePath}): {ex.Message}");
            errors.Add($"[Rsp:{Path.GetFileName(rspFilePath)}] Parse exception: {ex.Message}");
            return (null, null, references, analyzerPaths, errors);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Assembly> AnalyzerAssemblyCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// .rsp の /analyzer: で指定された Roslyn Incremental Source Generator DLL を実行し、
    /// 生成コードを合流させた新しい Compilation を返す。analyzerPaths が空の場合は
    /// compilation をそのまま返す。レガシー ISourceGenerator は非対応（IIncrementalGenerator のみ）。
    /// </summary>
    private static (CSharpCompilation Compilation, List<string> Diagnostics) RunSourceGenerators(
        CSharpCompilation compilation,
        IReadOnlyList<string> analyzerPaths,
        CSharpParseOptions parseOptions,
        INgolLogger? log)
    {
        var diagnostics = new List<string>();
        if (analyzerPaths.Count == 0)
            return (compilation, diagnostics);

        var generators = new List<IIncrementalGenerator>();
        foreach (var path in analyzerPaths)
        {
            try
            {
                var asm = AnalyzerAssemblyCache.GetOrAdd(path, Assembly.LoadFrom);
                foreach (var type in asm.GetExportedTypes())
                {
                    if (typeof(IIncrementalGenerator).IsAssignableFrom(type) && !type.IsAbstract
                        && type.GetConstructor(Type.EmptyTypes) != null)
                    {
                        generators.Add((IIncrementalGenerator)Activator.CreateInstance(type)!);
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add($"[Analyzer:{Path.GetFileName(path)}] Load failed: {ex.Message}");
                log?.LogWarning($"[RoslynCompiler] Analyzer load failed ({path}): {ex.Message}");
            }
        }

        if (generators.Count == 0)
            return (compilation, diagnostics);

        var driver = CSharpGeneratorDriver.Create(generators.ToArray())
            .WithUpdatedParseOptions(parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out var genDiagnostics);

        foreach (var d in genDiagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
            diagnostics.Add(d.ToString());

        log?.LogDebug($"[RoslynCompiler] Ran {generators.Count} source generator(s) from {analyzerPaths.Count} analyzer DLL(s)");

        return ((CSharpCompilation)newCompilation, diagnostics);
    }

    private static string? ExtractForwardedAssemblyName(string message)
    {
        const string marker = "forwarded to assembly '";
        var idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        var comma = message.IndexOf(',', start);
        if (comma < 0) return null;
        return message.Substring(start, comma - start).Trim();
    }

    /// <summary>
    /// 複数の .cs ソースを 1 つの DLL にコンパイルして <paramref name="outputDir"/> に保存する。
    /// NodeRegistry への登録は行わない（配布用 DLL の生成を目的とするため）。
    /// </summary>
    public static async Task<(bool Success, string? SavedPath, string? ErrorMessage)> CompileMultipleAndSaveAsync(
        IReadOnlyList<(string Source, string ClassName)> sources,
        string assemblyName,
        string outputDir,
        INgolLogger? log,
        IReadOnlyList<string>? rspFilePaths = null)
    {
        return await Task.Run(() => CompileMultipleAndSave(sources, assemblyName, outputDir, log, rspFilePaths));
    }

    private static (bool Success, string? SavedPath, string? ErrorMessage) CompileMultipleAndSave(
        IReadOnlyList<(string Source, string ClassName)> sources,
        string assemblyName,
        string outputDir,
        INgolLogger? log,
        IReadOnlyList<string>? rspFilePaths = null)
    {
        if (sources.Count == 0)
            return (false, null, "No source files to compile.");

        // 0. 選択ノードに紐づく .rsp があれば全てマージする（複数ノードで定義が競合する場合は和集合として扱う）
        var rspReferences = new List<MetadataReference>();
        var rspAnalyzerPaths = new List<string>();
        var mergedDefines = new List<string>();
        CSharpParseOptions? mergedParseOptions = null;
        if (rspFilePaths != null)
        {
            foreach (var rspPath in rspFilePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var (po, _, rspRefs, analyzerPaths, errs) = ParseRspFile(rspPath, assemblyName + ".cs", log);
                foreach (var e in errs) log?.LogWarning($"[RoslynCompiler] {e}");
                rspReferences.AddRange(rspRefs);
                rspAnalyzerPaths.AddRange(analyzerPaths);
                if (po != null)
                {
                    mergedDefines.AddRange(po.PreprocessorSymbolNames);
                    mergedParseOptions = po;
                }
            }
        }
        var effectiveParseOptions = mergedParseOptions != null
            ? mergedParseOptions.WithPreprocessorSymbols(mergedDefines.Union(ScriptPreprocessorSymbols).Distinct())
            : ScriptParseOptions;

        // 1. 全ソースの構文木を生成
        var syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s.Source, effectiveParseOptions, path: s.ClassName + ".cs"))
            .ToArray();

        // 2. 参照パスを収集して MetadataReference に変換
        var refPaths = BuildReferencePaths(log!);
        var refs = new List<MetadataReference>();
        foreach (var p in refPaths)
        {
            try { refs.Add(MetadataReference.CreateFromFile(p)); }
            catch { }
        }
        refs.Add(MetadataReference.CreateFromFile(typeof(INode).Assembly.Location));
        refs.AddRange(rspReferences);

        // 3. コンパイル
        var safeAsmName = SanitizeFileName(assemblyName);
        var compilation = CSharpCompilation.Create(
            assemblyName: safeAsmName,
            syntaxTrees: syntaxTrees.Concat(new[] { NullablePolyfillTree }).ToArray(),
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        if (rspAnalyzerPaths.Count > 0)
        {
            var (genCompilation, genDiagnostics) = RunSourceGenerators(compilation, rspAnalyzerPaths, effectiveParseOptions, log);
            compilation = genCompilation;
            foreach (var d in genDiagnostics) log?.LogWarning($"[RoslynCompiler] {d}");
        }

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
            var msg = string.Join("; ", errors);
            log?.LogWarning($"[RoslynCompiler] CompileMultiple failed: {msg}");
            return (false, null, msg);
        }

        // 4. DLL を保存
        try
        {
            Directory.CreateDirectory(outputDir);
            var dllPath = Path.Combine(outputDir, $"{safeAsmName}.dll");
            if (File.Exists(dllPath)) File.Delete(dllPath);
            File.WriteAllBytes(dllPath, ms.ToArray());
            log?.LogInfo($"[RoslynCompiler] Exported DLL: {dllPath}");
            return (true, dllPath, null);
        }
        catch (Exception ex)
        {
            log?.LogError($"[RoslynCompiler] Failed to save DLL: {ex.Message}");
            return (false, null, $"Failed to save DLL: {ex.Message}");
        }
    }
}
