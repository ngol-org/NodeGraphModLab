using NodeGraphModLab;
using NodeGraphModLab.HostLogging;

namespace NgolEmbedSample;

/// <summary>
/// NGOL起動処理そのもの。samples/NgolPluggableSample/NgolActivator.cs と同じ役割・
/// 同じメソッド形状(TryStart)を持つが、こちらは NodeGraphModLab.Core 等への
/// compile-time 参照を持つ(NgolEmbedSample.csproj の &lt;Reference&gt;、Private=false)。
/// 型を直接 new できるためコードは単純だが、代わりに「呼び出し時点でDLLが実際に
/// ロード済みであること」を呼び出し元(Program.cs)が保証する必要がある — このメソッド
/// 自体は NgolRuntime 等の型をIL上に直接持つため、未ロード状態で呼び出されると
/// JITコンパイル時に例外になり、Pluggable版のように「呼んでみてダメならnullを返す」
/// という安全な失敗ができない。DLL存在チェックがこのクラスではなくProgram.cs側に
/// あるのはこのため。
///
/// NGOL起動の条件は Core.dll + NodeAPI.dll + HostLogging.dll の3本すべて。
/// compile-time参照を持つ以上、HostLoggingだけ任意にする(無くても動く)ことはできない
/// ——これがreflectionベースのNgolPluggableSample(Core+NodeAPIの2本のみで足りる)との
/// 本質的な違い。
/// </summary>
internal static class NgolActivator
{
    /// <summary>
    /// NGOLを起動する。前提: Program.cs が Core/NodeAPI/HostLoggingの3DLLの存在確認と
    /// Assembly.LoadFromを既に済ませていること。戻り値の型をPluggable版とそろえるため
    /// nullable にしているが、この前提が満たされている限り Runtime が null になることはない。
    /// </summary>
    public static (IDisposable? Runtime, int? Port) TryStart(string ngolPluginDir)
    {
        // Step 2: ロガーを用意する。型を直接知っているので new するだけでよい
        // (Pluggable版はこれをreflection経由で行っている)。
        var logger = new ConsoleFileNgolLogger(Path.Combine(AppContext.BaseDirectory, "host.log"));

        // Step 3: 起動オプションを組み立てる。EnableDirectMode=true は「フレーム駆動の
        // Updateコールバックが無いホスト（このサンプルのようなコンソールアプリ）向けに、NGOL内部で
        // 専用スレッドを立ててポーリングする」モード。フレーム駆動のUpdateコールバックを持つホスト
        // ではfalseにする（詳細は NgolRuntimeOptions.cs のXMLコメント参照）。
        var options = new NgolRuntimeOptions
        {
            EnableDirectMode = true,
            PluginVersion = "NgolEmbedSample",
            GameName = "NgolEmbedSample",
        };

        // Step 4: NgolRuntime を生成し、Initialize(pluginDir) で起動する。
        // ここが実際のNGOL本体の起動処理そのもの（WebUIサーバー・ノード registry・
        // ホットリロード監視などがすべてこの一呼び出しで立ち上がる）。
        // 型を直接知っているので new NgolRuntime(...) と素直に書ける
        // (Pluggable版は Activator.CreateInstance + MethodInfo.Invoke で同じことを行っている)。
        var runtime = new NgolRuntime(logger, options);
        runtime.Initialize(ngolPluginDir);

        // WebUIの接続先URL表示用。ポート番号は ngol-plugin/ngol-config.json (無ければ既定11156)
        // から NgolConfig が読み込んだ値。
        var port = NgolConfig.Port;

        return (runtime, port);
    }
}
