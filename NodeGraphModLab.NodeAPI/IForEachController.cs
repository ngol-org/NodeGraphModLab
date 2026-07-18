namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// ForEach ループコントローラーのマーカーインターフェース。
/// このインターフェースを実装したノードは GraphExecutor によってループ展開される。
/// </summary>
public interface IForEachController
{
    /// <summary>
    /// 入力ポートの値からループするアイテムリストを取得する。
    /// </summary>
    IReadOnlyList<object?> GetItems(IReadOnlyDictionary<string, object?> inputValues);

    /// <summary>現在のアイテムを出力するポート名。</summary>
    string CurrentItemPortName { get; }

    /// <summary>現在のループインデックスを出力するポート名。</summary>
    string IndexPortName { get; }
}
