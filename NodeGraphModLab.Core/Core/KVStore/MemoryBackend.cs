namespace NodeGraphModLab.Core.KVStore;

/// <summary>
/// 永続化しないバックエンド。値は KVStore 本体のメモリキャッシュにのみ載り、プロセス終了で失われる。
///
/// 保存先を用意できない環境や、保存させたくない場面で明示的に選ぶ。
/// ディスクへ書けない状況での最終的な退避先としても使う（起動自体は続行できる）。
/// </summary>
internal sealed class MemoryBackend : IKVStoreBackend
{
    public IEnumerable<(string Key, string ValueJson)> LoadAll() => Array.Empty<(string, string)>();

    public void Upsert(string key, string valueJson) { }

    public void Delete(string key) { }

    public void Dispose() { }
}
