namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// ノード間で共有できるキーバリューストア。
/// ゲームセッションをまたいで値を永続保持する。
/// ctx.Store 経由でどのノードからも同一インスタンスにアクセスできる。
/// </summary>
public interface IKVStore
{
    /// <summary>値をセットする。ゲーム再起動後も保持される。</summary>
    void Set(string key, object? value);

    /// <summary>値を取得する。存在しない場合は null。</summary>
    object? Get(string key);

    /// <summary>型付きで値を取得する。存在しない・型変換不可の場合は default。</summary>
    T? Get<T>(string key);

    /// <summary>型付きで取得を試みる。</summary>
    bool TryGet<T>(string key, out T? value);

    /// <summary>キーの存在確認。</summary>
    bool ContainsKey(string key);

    /// <summary>エントリを削除する。</summary>
    void Delete(string key);

    /// <summary>指定プレフィックスで始まる全キーを返す（null なら全件）。</summary>
    IEnumerable<string> Keys(string? prefix = null);
}
