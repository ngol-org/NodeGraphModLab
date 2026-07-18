namespace NodeGraphModLab;

/// <summary>
/// NGOL インフラ層が使用するロガー抽象。
/// ホスト固有のロガー実装（コンソール出力・ファイル出力・ホストのログシステムへの委譲等）を隠蔽する。
/// </summary>
public interface INgolLogger
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogDebug(string message);
}
