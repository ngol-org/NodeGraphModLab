using System.Reflection;

namespace NgolPluggableSample;

/// <summary>
/// NGOL起動処理そのもの。既存プロジェクトへ組み込む場合は、この1ファイルをコピーして
/// <c>NgolActivator.TryStart(pluginDir)</c> を1回呼ぶだけでよい（コンソールループ等の
/// アプリ固有の処理は含まない。それは Program.cs 側の責務）。
///
/// NodeGraphModLab.Core / HostLogging への型参照をプロジェクトから一切持たない
/// （NgolPluggableSample.csproj に Reference は無い）。NGOL起動の条件は
/// Core.dll + NodeAPI.dll の2本のみ。HostLogging.dll はコンソール+ファイルの
/// 別途ロガーが欲しい場合のオプション同梱であり、無くてもNGOLは起動する
/// （その場合は DispatchProxy による簡易コンソールロガーにフォールバックする）。
/// </summary>
internal static class NgolActivator
{
    /// <summary>
    /// NGOLの起動を試みる。pluginDir に Core.dll / NodeAPI.dll が無ければ何もせず
    /// (null, null) を返す(呼び出し元は通常のアプリとしてそのまま続行してよい)。
    /// 起動できた場合、Runtime を Dispose() すると NGOL を停止できる。
    /// </summary>
    public static (IDisposable? Runtime, int? Port) TryStart(string ngolPluginDir)
    {
        var nodeApiDll = Path.Combine(ngolPluginDir, "NodeGraphModLab.NodeAPI.dll");
        var coreDll = Path.Combine(ngolPluginDir, "NodeGraphModLab.Core.dll");
        var hostLoggingDll = Path.Combine(ngolPluginDir, "NodeGraphModLab.HostLogging.dll");

        if (!File.Exists(nodeApiDll) || !File.Exists(coreDll))
        {
            Console.WriteLine($"[NGOL] Core/NodeAPI DLL not found under {ngolPluginDir}. Continuing without NGOL.");
            return (null, null);
        }

        // Step 1: NGOL本体アセンブリをロードする。
        // このプロジェクトはNodeGraphModLab.Core/NodeAPIをコンパイル時参照していないため
        // （csprojにReferenceが無い）、実行時に Assembly.LoadFrom でロードするまでは
        // NgolRuntime 等の型はプロセス内のどこにも存在しない。
        Assembly.LoadFrom(nodeApiDll);
        var coreAsm = Assembly.LoadFrom(coreDll);

        // Step 2: ロガーを用意する。NgolRuntimeのコンストラクタは INgolLogger を要求するが、
        // このプロジェクトはその型さえコンパイル時に知らないので、具象インスタンスの生成は
        // すべて reflection (Type.GetType / Activator.CreateInstance) 経由になる。
        var logger = ResolveLogger(coreAsm, hostLoggingDll);

        // Step 3: 起動オプションを組み立てる。EnableDirectMode=true は「フレーム駆動の
        // Updateコールバックが無いホスト（このサンプルのようなコンソールアプリ）向けに、NGOL内部で
        // 専用スレッドを立ててポーリングする」モード。フレーム駆動のUpdateコールバックを持つホスト
        // ではfalseにする（詳細は NgolRuntimeOptions.cs のXMLコメント参照）。
        var optionsType = coreAsm.GetType("NodeGraphModLab.NgolRuntimeOptions", throwOnError: true)!;
        var options = Activator.CreateInstance(optionsType)!;
        optionsType.GetProperty("EnableDirectMode")!.SetValue(options, true);
        optionsType.GetProperty("PluginVersion")!.SetValue(options, "NgolPluggableSample");
        optionsType.GetProperty("GameName")!.SetValue(options, "NgolPluggableSample");

        // Step 4: NgolRuntime を生成し、Initialize(pluginDir) で起動する。
        // ここが実際のNGOL本体の起動処理そのもの（WebUIサーバー・ノード registry・
        // ホットリロード監視などがすべてこの一呼び出しで立ち上がる）。
        // 型参照が無いため new NgolRuntime(...) とは書けず、Activator.CreateInstance +
        // MethodInfo.Invoke で同じことを行っている。
        var runtimeType = coreAsm.GetType("NodeGraphModLab.NgolRuntime", throwOnError: true)!;
        var runtime = (IDisposable)Activator.CreateInstance(runtimeType, logger, options)!;
        runtimeType.GetMethod("Initialize")!.Invoke(runtime, new object[] { ngolPluginDir });

        // WebUIの接続先URL表示用。ポート番号は ngol-plugin/ngol-config.json (無ければ既定11156)
        // から NgolConfig が読み込んだ値を、これも reflection 経由で取得する。
        var port = (int)coreAsm.GetType("NodeGraphModLab.NgolConfig", throwOnError: true)!
            .GetProperty("Port", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        return (runtime, port);
    }

    /// <summary>
    /// ロガー登録: HostLogging.dll (任意同梱)があればコンソール+ファイル出力ロガーを使い、
    /// 無ければ DispatchProxy による簡易コンソールロガーにフォールバックする。
    /// </summary>
    private static object ResolveLogger(Assembly coreAsm, string hostLoggingDll)
    {
        if (File.Exists(hostLoggingDll))
            return CreateHostLoggingLogger(hostLoggingDll);

        var loggerInterfaceType = coreAsm.GetType("NodeGraphModLab.INgolLogger", throwOnError: true)!;
        return CreateFallbackConsoleLogger(loggerInterfaceType);
    }

    /// <summary>HostLogging.dll (任意同梱)が存在する場合、そちらのコンソール+ファイル出力ロガーを使う。</summary>
    private static object CreateHostLoggingLogger(string hostLoggingDll)
    {
        var hostLoggingAsm = Assembly.LoadFrom(hostLoggingDll);
        var loggerType = hostLoggingAsm.GetType("NodeGraphModLab.HostLogging.ConsoleFileNgolLogger", throwOnError: true)!;
        return Activator.CreateInstance(loggerType, Path.Combine(AppContext.BaseDirectory, "host.log"))!;
    }

    /// <summary>
    /// HostLogging.dll が無い場合のフォールバック。DispatchProxy で INgolLogger を
    /// 実行時に動的実装し、コンソールへ書き出すだけの最小ロガーを作る。
    /// </summary>
    private static object CreateFallbackConsoleLogger(Type loggerInterfaceType)
    {
        Console.WriteLine("[NGOL] HostLogging.dll not found. Falling back to a minimal console logger.");
        var createMethod = typeof(DispatchProxy)
            .GetMethod(nameof(DispatchProxy.Create))!
            .MakeGenericMethod(loggerInterfaceType, typeof(FallbackConsoleLoggerProxy));
        return createMethod.Invoke(null, null)!;
    }
}

/// <summary>
/// INgolLogger (NodeGraphModLab.Core.dll) をコンパイル時参照なしに実装するための
/// DispatchProxy。実際のインターフェース型は NgolActivator が reflection で解決する。
/// </summary>
public class FallbackConsoleLoggerProxy : DispatchProxy
{
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
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
        return null;
    }
}
