using System.Text;
using NodeGraphModLab;

namespace NodeGraphModLab.HostLogging;

/// <summary>
/// 標準的なホスト向けの <see cref="INgolLogger"/> 実装。
/// コンソール出力 + ログファイル追記を行う。
/// </summary>
public sealed class ConsoleFileNgolLogger : INgolLogger
{
    private static readonly UTF8Encoding FileEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _filePath;
    private readonly object _fileLock = new();
    private readonly bool _consoleAvailable;

    public ConsoleFileNgolLogger(string filePath)
    {
        _filePath = filePath;

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // セッション毎に切り詰める（今回起動分のログという扱い）
        File.WriteAllText(filePath, string.Empty, FileEncoding);

        _consoleAvailable = TryPrepareConsole();
    }

    private static bool TryPrepareConsole()
    {
        try
        {
            Console.OutputEncoding = FileEncoding;
            return true;
        }
        catch
        {
            // WinExe 等でコンソールが割り当てられていない場合はここで失敗する。
            // コンソール出力はあくまで「ターミナルから起動された場合のおまけ」のため、
            // ファイル出力側に影響を与えないよう握り潰す。
            return false;
        }
    }

    public void LogInfo(string message)    => Emit("INFO", message);
    public void LogWarning(string message) => Emit("WARN", message);
    public void LogError(string message)   => Emit("ERROR", message);
    public void LogDebug(string message)   => Emit("DEBUG", message);

    private void Emit(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";

        lock (_fileLock)
        {
            File.AppendAllText(_filePath, line + Environment.NewLine, FileEncoding);
        }

        if (_consoleAvailable)
        {
            try { Console.WriteLine(line); }
            catch { /* コンソールが後から切断された場合も無視する */ }
        }
    }
}
