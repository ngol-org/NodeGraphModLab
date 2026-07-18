namespace NgolPluggableSample;

internal static class Program
{
    private static void Main(string[] args)
    {
        var ngolPluginDir = ResolveNgolPluginDir(args);
        var autoStart = args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);

        // NGOL起動処理そのものは NgolActivator.TryStart(pluginDir) の1呼び出しに集約されている。
        // DLLが無ければ (null, null) が返るだけで、このアプリ自体は普通に動き続けられる。
        var (runtime, port) = NgolActivator.TryStart(ngolPluginDir);

        if (runtime is null)
        {
            RunConsoleLoop(autoStart, "Started without NGOL.");
            return;
        }

        try
        {
            RunConsoleLoop(autoStart, $"WebUI : http://127.0.0.1:{port}/");
        }
        finally
        {
            runtime.Dispose();
        }
    }

    private const string NgolCoreDllName = "NodeGraphModLab.Core.dll";

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
