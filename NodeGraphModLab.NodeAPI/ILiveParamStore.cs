namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// 永続ノード向けライブパラメータストア。
/// WebUI の push_node_live_params により更新され、
/// IExecutionContext.GetLiveParam で読み取る。
/// </summary>
public interface ILiveParamStore
{
    /// <summary>指定ノードの params を既存値にマージする。</summary>
    IReadOnlyList<string> MergeParams(string nodeInstanceId, IReadOnlyDictionary<string, object?> parameters);

    /// <summary>キーが存在すれば値を返す。</summary>
    bool TryGet(string nodeInstanceId, string key, out object? value);

    /// <summary>ノード停止時にそのノードのエントリを削除する。</summary>
    void ClearNode(string nodeInstanceId);

    /// <summary>全ノードのエントリを削除する。</summary>
    void ClearAll();
}
