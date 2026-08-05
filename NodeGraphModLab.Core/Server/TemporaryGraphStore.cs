using System.Collections.Generic;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

/// <summary>
/// ディスクへ保存せずにグラフを一時的に置く場所。プロセスが生きている間だけ保持する。
///
/// 用途は「試作したグラフを保存せずキャンバスへ開く」こと。
/// グラフを開く経路は id しか運ばないため、id の解決先をここへ増やすだけで実現できる
/// （<see cref="Handlers.LoadGraphHandler"/> がディスクより先にここを引く）。
///
/// 保存済み一覧の走査対象には含めない。一時グラフが一覧に並ばないのは意図した動作。
/// </summary>
internal sealed class TemporaryGraphStore
{
    /// <summary>
    /// 保持する上限。超えたら登録の古い順に捨てる。
    /// 際限なく持つと、試行の回数だけメモリが増えるため。
    /// </summary>
    private const int Capacity = 100;

    private readonly object _lock = new();
    private readonly Dictionary<string, NodeGraph> _graphs = new();
    private readonly LinkedList<string> _order = new();  // 先頭が最も古い

    /// <summary>
    /// 一時グラフを登録する。同じ id が既にあれば置き換える。
    /// </summary>
    public void Add(string id, NodeGraph graph)
    {
        lock (_lock)
        {
            if (_graphs.ContainsKey(id)) _order.Remove(id);
            _graphs[id] = graph;
            _order.AddLast(id);

            while (_order.Count > Capacity)
            {
                var oldest = _order.First!.Value;
                _order.RemoveFirst();
                _graphs.Remove(oldest);
            }
        }
    }

    /// <summary>登録済みなら取り出す。無ければ null。</summary>
    public NodeGraph? TryGet(string id)
    {
        lock (_lock)
        {
            return _graphs.TryGetValue(id, out var g) ? g : null;
        }
    }

    public int Count
    {
        get { lock (_lock) { return _graphs.Count; } }
    }
}
