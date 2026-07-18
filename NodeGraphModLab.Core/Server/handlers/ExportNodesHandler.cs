using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// 選択ノードの .cs ソースを 1 つの DLL にまとめてエクスポートするハンドラー。
/// type = "export_nodes"
/// </summary>
internal sealed class ExportNodesHandler : IMessageHandler
{
    private readonly HandlerContext _ctx;
    public string MessageType => "export_nodes";

    public ExportNodesHandler(HandlerContext ctx) { _ctx = ctx; }

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var assemblyName = root.TryGetProperty("assemblyName", out var an) ? an.GetString() : null;
        var outputDirRel = root.TryGetProperty("outputDir", out var od) ? od.GetString() : null;
        var nodeTypeIds = root.TryGetProperty("nodeTypeIds", out var ids)
            ? ids.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
            : new List<string>();

        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            var fail = new ExportNodesResponse { Success = false, ErrorMessage = "assemblyName is required." };
            await session.SendAsync(JsonSerializer.Serialize(fail, ServerJsonContext.Default.ExportNodesResponse));
            return;
        }

        if (nodeTypeIds.Count == 0)
        {
            var fail = new ExportNodesResponse { Success = false, ErrorMessage = "nodeTypeIds is empty." };
            await session.SendAsync(JsonSerializer.Serialize(fail, ServerJsonContext.Default.ExportNodesResponse));
            return;
        }

        // outputDir は plugins フォルダ相対パスまたは絶対パス。
        // セキュリティ: 絶対パスは受け付けず、必ず _pluginsDir 以下に解決する。
        var pluginsDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location)
            ?? AppDomain.CurrentDomain.BaseDirectory;

        var relDir = outputDirRel ?? "Nodes/CustomNodes/dll";
        // Path traversal 対策: ".." を含むパスは拒否
        if (relDir.Contains(".."))
        {
            var fail = new ExportNodesResponse { Success = false, ErrorMessage = "outputDir must not contain '..'." };
            await session.SendAsync(JsonSerializer.Serialize(fail, ServerJsonContext.Default.ExportNodesResponse));
            return;
        }
        var outputDir = Path.IsPathRooted(relDir) ? relDir : Path.Combine(pluginsDir, relDir.Replace('/', Path.DirectorySeparatorChar));

        var sources = new List<(string Source, string ClassName)>();
        var skipped = new List<string>();
        // 同一ファイルを重複して読まないよう追跡（1ファイル複数クラス対応、.srclist経由の共有ファイルも含む）
        var includedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rspFilePaths = new List<string>();

        async Task<bool> TryAddSourceAsync(string path)
        {
            if (includedFiles.Contains(path)) return true; // 既に追加済み
            if (!File.Exists(path)) return false;
            try
            {
                includedFiles.Add(path);
#if NET6_0_OR_GREATER
                var src = await File.ReadAllTextAsync(path);
#else
                var src = await System.Threading.Tasks.Task.Run(() => File.ReadAllText(path));
#endif
                sources.Add((src, Path.GetFileNameWithoutExtension(path)));
                return true;
            }
            catch (Exception ex)
            {
                _ctx.Log?.LogWarning($"[ExportNodes] Failed to read {path}: {ex.Message}");
                return false;
            }
        }

        foreach (var typeId in nodeTypeIds)
        {
            // _ctx.ScriptNodeId は nodeTypeId → filePath 方向
            if (!_ctx.ScriptNodeId.TryGetValue(typeId, out var filePath) || !File.Exists(filePath))
            {
                _ctx.Log?.LogWarning($"[ExportNodes] No .cs source for nodeTypeId={typeId} — skipped");
                skipped.Add(typeId);
                continue;
            }

            if (!await TryAddSourceAsync(filePath))
            {
                skipped.Add(typeId);
                continue;
            }

            // .srclist で解決される追加ソースファイルも同梱する（フォルダを跨いだ共有も相対パスで解決される）
            var srclistPath = Path.ChangeExtension(filePath, ".srclist");
            if (File.Exists(srclistPath))
            {
                foreach (var extraPath in RoslynCompiler.ResolveSrclist(srclistPath, _ctx.Log))
                    await TryAddSourceAsync(extraPath);
            }

            // .rsp（コンパイラオプション）があれば併せてマージ対象に加える
            var rspPath = Path.ChangeExtension(filePath, ".rsp");
            if (File.Exists(rspPath))
                rspFilePaths.Add(rspPath);
        }

        if (sources.Count == 0)
        {
            var fail = new ExportNodesResponse
            {
                Success = false,
                SkippedTypeIds = skipped,
                ErrorMessage = "No compilable .cs sources found for the selected nodes.",
            };
            await session.SendAsync(JsonSerializer.Serialize(fail, ServerJsonContext.Default.ExportNodesResponse));
            return;
        }

        var (success, savedPath, errorMsg) = await RoslynCompiler.CompileMultipleAndSaveAsync(
            sources, assemblyName, outputDir, _ctx.Log, rspFilePaths.Count > 0 ? rspFilePaths : null);

        var response = new ExportNodesResponse
        {
            Success = success,
            SavedPath = savedPath,
            SkippedTypeIds = skipped.Count > 0 ? skipped : null,
            ErrorMessage = errorMsg,
        };
        await session.SendAsync(JsonSerializer.Serialize(response, ServerJsonContext.Default.ExportNodesResponse));
    }
}
