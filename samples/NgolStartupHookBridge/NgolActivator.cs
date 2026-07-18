using System.Reflection;

namespace NgolStartupHookBridge;

/// <summary>
/// NGOL起動処理そのもの（<c>samples/NgolPluggableSample/NgolActivator.cs</c> の起動ロジックを
/// StartupHook経由での呼び出し向けに適応したもの）。
/// NodeGraphModLab.Core / HostLogging への型参照をプロジェクトから一切持たない。
/// StartupHook.Initialize() は引数を受け取れないため、pluginDir の解決はコマンドライン引数ではなく
/// 環境変数 <c>NGOL_BRIDGE_PLUGIN_DIR</c>（無ければこのDLLと同じ場所の ngol-plugin/）で行う。
/// </summary>
internal static class NgolActivator
{
    private const string NodeApiDllName = "NodeGraphModLab.NodeAPI.dll";
    private const string CoreDllName = "NodeGraphModLab.Core.dll";
    private const string HostLoggingDllName = "NodeGraphModLab.HostLogging.dll";

    // StartupHook.Initialize() から戻った後もプロセス生存中はNGOLを動かし続ける必要があるため、
    // GCで回収されないよう静的フィールドで参照を保持する。
    private static object? _runtime;

    public static void TryStart()
    {
        var asmDir = Path.GetDirectoryName(typeof(NgolActivator).Assembly.Location) ?? AppContext.BaseDirectory;
        var bridgeLogPath = Path.Combine(asmDir, "startup-hook-bridge.log");

        var pluginDir = ResolvePluginDir(asmDir);
        var nodeApiDll = Path.Combine(pluginDir, NodeApiDllName);
        var coreDll = Path.Combine(pluginDir, CoreDllName);
        var hostLoggingDll = Path.Combine(pluginDir, HostLoggingDllName);

        if (!File.Exists(nodeApiDll) || !File.Exists(coreDll))
        {
            AppendBridgeLog(bridgeLogPath, $"Core/NodeAPI DLL not found under {pluginDir}. Skipping NGOL startup.");
            return;
        }

        // Step 1: NGOL本体アセンブリをロードする。このプロジェクトはCore/NodeAPIを
        // コンパイル時参照していないため、ここまでは NgolRuntime 等の型はプロセス内に存在しない。
        Assembly.LoadFrom(nodeApiDll);
        var coreAsm = Assembly.LoadFrom(coreDll);

        // Step 2: ロガーを用意する。
        var logger = ResolveLogger(coreAsm, hostLoggingDll, asmDir);

        // Step 3: 起動オプション。EnableDirectMode=true はフレーム駆動のUpdateコールバックが無い
        // ホスト向けに、NGOL内部で専用スレッドを立ててポーリングするモード。
        var optionsType = coreAsm.GetType("NodeGraphModLab.NgolRuntimeOptions", throwOnError: true)!;
        var options = Activator.CreateInstance(optionsType)!;
        optionsType.GetProperty("EnableDirectMode")!.SetValue(options, true);
        optionsType.GetProperty("PluginVersion")!.SetValue(options, "NgolStartupHookBridge");
        optionsType.GetProperty("GameName")!.SetValue(options, "NgolStartupHookBridge");

        // Step 4: NgolRuntime を生成し起動する。
        var runtimeType = coreAsm.GetType("NodeGraphModLab.NgolRuntime", throwOnError: true)!;
        var runtime = Activator.CreateInstance(runtimeType, logger, options)!;
        runtimeType.GetMethod("Initialize")!.Invoke(runtime, new object[] { pluginDir });

        _runtime = runtime;

        AppendBridgeLog(bridgeLogPath, $"NGOL started via StartupHook. pluginDir={pluginDir}");
    }

    private static string ResolvePluginDir(string asmDir)
    {
        var envDir = Environment.GetEnvironmentVariable("NGOL_BRIDGE_PLUGIN_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return Path.GetFullPath(envDir);

        // 配布形態: ブリッジDLLと同じディレクトリに ngol-plugin/ を同梱する想定。
        var sibling = Path.Combine(asmDir, "ngol-plugin");
        if (Directory.Exists(sibling))
            return sibling;

        // 開発時実行 (bin/Debug|Release/net6.0/ から見た) フォールバック先。
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "ngol-plugin"));
    }

    /// <summary>
    /// HostLogging.dll (任意同梱) があればコンソール+ファイル出力ロガーを使い、
    /// 無ければファイルのみに出力するフォールバックロガーを使う。
    /// StartupHook経由の起動では対象アプリがコンソールを掴んでいるとは限らないため、
    /// NgolPluggableSample と異なりコンソール出力には依存しない設計にする。
    /// </summary>
    private static object ResolveLogger(Assembly coreAsm, string hostLoggingDll, string asmDir)
    {
        if (File.Exists(hostLoggingDll))
        {
            var hostLoggingAsm = Assembly.LoadFrom(hostLoggingDll);
            var loggerType = hostLoggingAsm.GetType("NodeGraphModLab.HostLogging.ConsoleFileNgolLogger", throwOnError: true)!;
            return Activator.CreateInstance(loggerType, Path.Combine(asmDir, "host.log"))!;
        }

        var loggerInterfaceType = coreAsm.GetType("NodeGraphModLab.INgolLogger", throwOnError: true)!;
        var createMethod = typeof(DispatchProxy)
            .GetMethod(nameof(DispatchProxy.Create))!
            .MakeGenericMethod(loggerInterfaceType, typeof(FileOnlyLoggerProxy));
        var proxy = createMethod.Invoke(null, null)!;
        ((FileOnlyLoggerProxy)proxy).LogFilePath = Path.Combine(asmDir, "host.log");
        return proxy;
    }

    private static void AppendBridgeLog(string path, string message)
    {
        try
        {
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // ログ出力自体の失敗はStartupHookへ伝播させない。
        }
    }
}

/// <summary>
/// INgolLogger (NodeGraphModLab.Core.dll) をコンパイル時参照なしに実装するための DispatchProxy。
/// HostLogging.dll 非同梱時のフォールバック。コンソールには依存せずファイルのみへ出力する。
/// </summary>
public class FileOnlyLoggerProxy : DispatchProxy
{
    private static readonly object FileLock = new();

    public string LogFilePath { get; set; } = "host.log";

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var level = targetMethod?.Name switch
        {
            "LogWarning" => "WARN",
            "LogError" => "ERROR",
            "LogDebug" => "DEBUG",
            _ => "INFO",
        };
        var message = args is { Length: > 0 } ? args[0] as string : null;
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";

        try
        {
            lock (FileLock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // ログ出力自体の失敗はNGOL本体の動作に影響させない。
        }

        return null;
    }
}
