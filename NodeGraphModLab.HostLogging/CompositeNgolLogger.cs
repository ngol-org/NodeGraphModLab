using NodeGraphModLab;

namespace NodeGraphModLab.HostLogging;

/// <summary>
/// 複数の <see cref="INgolLogger"/> へ同時に委譲する薄いラッパー。
/// ホスト固有のロガー（画面表示等）と <see cref="ConsoleFileNgolLogger"/> を併用する際に使う。
/// 1つのシンクが例外を投げても他のシンクへの転送は継続する。
/// </summary>
public sealed class CompositeNgolLogger : INgolLogger
{
    private readonly INgolLogger[] _sinks;

    public CompositeNgolLogger(params INgolLogger[] sinks) => _sinks = sinks;

    public void LogInfo(string message)    => ForEach(l => l.LogInfo(message));
    public void LogWarning(string message) => ForEach(l => l.LogWarning(message));
    public void LogError(string message)   => ForEach(l => l.LogError(message));
    public void LogDebug(string message)   => ForEach(l => l.LogDebug(message));

    private void ForEach(Action<INgolLogger> action)
    {
        foreach (var sink in _sinks)
        {
            try { action(sink); }
            catch { /* 1シンクの失敗が他のシンクへの転送を妨げないようにする */ }
        }
    }
}
