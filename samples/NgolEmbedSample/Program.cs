using NodeGraphModLab;
using NodeGraphModLab.HostLogging;

namespace NgolEmbedSample;

/// <summary>
/// NGOL を通常のライブラリとして組み込む最小サンプル。
/// 既存アプリへ組み込むときも、起動処理へ同じ数行を足すだけでよい。
/// </summary>
internal static class Program
{
    private static void Main()
    {
        // NGOLリソース（Nodes/・WebUI/・ngol-config.json）を置いたフォルダ。
        // csproj がビルド出力へコピーするので、実行ファイルの隣にある。
        var ngolRoot = Path.Combine(AppContext.BaseDirectory, "ngol-resources");

        var logger = new ConsoleFileNgolLogger(Path.Combine(AppContext.BaseDirectory, "host.log"));
        var options = new NgolRuntimeOptions
        {
            // true のとき NGOL が内部スレッドで駆動する。
            // false にする場合はホストが自身のループから runtime.Tick() を毎回呼ぶこと。
            EnableDirectMode = true,
            PluginVersion = "NgolEmbedSample",
            GameName = "NgolEmbedSample",
        };
        var runtime = new NgolRuntime(logger, options);
        runtime.Initialize(ngolRoot);

        Console.WriteLine();
        Console.WriteLine("Running. Press Enter to stop.");
        Console.ReadLine();

        runtime.Dispose();
    }
}
