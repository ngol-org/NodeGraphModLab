namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// カスタムノードの実装インターフェース。
/// カスタムノードを作成する場合はこのインターフェースを実装し、
/// [NodeType] 属性を付与してください。
/// </summary>
public interface INode
{
    /// <summary>ノードを実行する。</summary>
    /// <param name="ctx">実行コンテキスト（ホスト内部状態へのアクセス、ログ出力等）</param>
    void Execute(IExecutionContext ctx);
}
