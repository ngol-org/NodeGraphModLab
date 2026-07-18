using System.Reflection;
using System.Runtime.CompilerServices;

namespace NgolEmbedSample;

internal static class Program
{
    private const string NgolCoreDllName = "NodeGraphModLab.Core.dll";

    private static void Main(string[] args)
    {
        var ngolPluginDir = ResolveNgolPluginDir(args);
        var autoStart = args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);

        var nodeApiDll = Path.Combine(ngolPluginDir, "NodeGraphModLab.NodeAPI.dll");
        var coreDll = Path.Combine(ngolPluginDir, NgolCoreDllName);
        var hostLoggingDll = Path.Combine(ngolPluginDir, "NodeGraphModLab.HostLogging.dll");

        // NgolActivator.TryStart は NgolRuntime 等の型をIL上に直接持つため、存在チェックは
        // 型に触れる前のここで済ませる必要がある。DLLが未ロードのまま TryStart を呼ぶと
        // JITコンパイル時に例外になり、Pluggable版のような「呼んでみてダメならnullを返す」
        // という安全な失敗ができない（これが直接参照方式の制約。詳細はNgolActivator.cs参照）。
        if (!File.Exists(nodeApiDll) || !File.Exists(coreDll) || !File.Exists(hostLoggingDll))
        {
            Console.WriteLine($"[NGOL] Core/NodeAPI/HostLogging DLL not found under {ngolPluginDir}. Continuing without NGOL.");
            RunConsoleLoop(autoStart, "Started without NGOL.");
            return;
        }

        // NGOL本体アセンブリを明示的にロードする。Core/NodeAPI/HostLoggingへの<Reference>は
        // コンパイル時の型解決専用（Private=false）で、実行時の実体ロードはここで行う必要がある。
        // NoInlining は、このメソッドがMainへインライン展開されることでロード順序の前提が
        // 崩れるのを防ぐ（このメソッド自体はNgolRuntime等の型に一切触れない）。
        LoadCoreAssemblies(nodeApiDll, coreDll, hostLoggingDll);

        // NGOL起動処理そのものは NgolActivator.TryStart(pluginDir) の1呼び出しに集約されている。
        var (runtime, port) = NgolActivator.TryStart(ngolPluginDir);

        try
        {
            RunConsoleLoop(autoStart, $"WebUI : http://127.0.0.1:{port}/");
        }
        finally
        {
            runtime!.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadCoreAssemblies(string nodeApiDll, string coreDll, string hostLoggingDll)
    {
        Assembly.LoadFrom(nodeApiDll);
        Assembly.LoadFrom(coreDll);
        Assembly.LoadFrom(hostLoggingDll);
    }

    private static string ResolveNgolPluginDir(string[] args)
    {
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
            return Path.GetFullPath(args[0]);

        var exeDir = AppContext.BaseDirectory;

        // 配布された実行ファイル向け: exe自身のディレクトリ、またはその直下のサブディレクトリに
        // NodeGraphModLab.Core.dll (NGOLコアDLL)があれば、そのディレクトリをpluginDirとして
        // 採用する。NodeAPI.dll等の他のDLLも同じディレクトリに揃っている前提。
        var discovered = FindNgolPluginDirNear(exeDir);
        if (discovered is not null)
            return discovered;

        // 開発時実行（dotnet run, bin/Debug|Release/net6.0/ から見た）フォールバック先。
        // bin/obj の外にあるため、dotnet clean 等でビルド成果物を消してもここには影響しない。
        return Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "ngol-plugin"));
    }

    private static string? FindNgolPluginDirNear(string exeDir)
    {
        if (File.Exists(Path.Combine(exeDir, NgolCoreDllName)))
            return exeDir;

        foreach (var subDir in Directory.EnumerateDirectories(exeDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (File.Exists(Path.Combine(subDir, NgolCoreDllName)))
                return subDir;
        }

        return null;
    }

    /// <summary>
    /// このサンプル自身の待機ループ（NGOLとは無関係。既存プロジェクトへ組み込む場合は
    /// 自前のメインループ/ゲームループをそのまま使えばよく、これは不要）。
    /// </summary>
    private static void RunConsoleLoop(bool autoStart, string statusLine)
    {
        var stopRequested = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopRequested = true;
        };

        Console.WriteLine();
        Console.WriteLine(statusLine);
        // 標準入力がリダイレクトされている環境（パイプ・サービス・CI等）では
        // Console.KeyAvailable が InvalidOperationException を投げるため、キー入力待ちを行わない。
        var waitWithoutKeyInput = autoStart || Console.IsInputRedirected;

        Console.WriteLine(waitWithoutKeyInput
            ? "Running. Press Ctrl+C (or terminate the process) to stop."
            : "Press Enter to stop, or Ctrl+C.");

        if (waitWithoutKeyInput)
        {
            while (!stopRequested) Thread.Sleep(200);
            return;
        }

        while (!stopRequested)
        {
            if (Console.KeyAvailable)
            {
                if (Console.ReadKey(intercept: true).Key == ConsoleKey.Enter) break;
                continue;
            }
            Thread.Sleep(50);
        }
    }
}
