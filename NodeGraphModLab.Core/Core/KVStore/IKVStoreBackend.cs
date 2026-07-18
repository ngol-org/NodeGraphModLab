namespace NodeGraphModLab.Core.KVStore;

/// <summary>
/// KVStore の永続化バックエンド。
/// LiteDB / JSON ファイル等に差し替え可能。
/// </summary>
internal interface IKVStoreBackend : IDisposable
{
    /// <summary>起動時に全エントリを読み込む。</summary>
    IEnumerable<(string Key, string ValueJson)> LoadAll();

    /// <summary>エントリを保存（upsert）する。</summary>
    void Upsert(string key, string valueJson);

    /// <summary>エントリを削除する。</summary>
    void Delete(string key);
}
