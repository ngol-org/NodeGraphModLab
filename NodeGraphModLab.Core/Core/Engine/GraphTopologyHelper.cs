using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Core.Engine;

/// <summary>
/// グラフトポロジー計算ユーティリティ。
/// トポロジカルソートおよび Union-Find による断片自動導出を担当する。
/// </summary>
public static class GraphTopologyHelper
{
    /// <summary>
    /// カーン法によるトポロジカルソート。
    /// 循環グラフを検出した場合はエラーメッセージを返す。
    /// </summary>
    public static (List<string> sorted, string? error) TopologicalSort(NodeGraph graph)
    {
        var nodeIds = graph.Nodes.Select(n => n.InstanceId).ToHashSet();
        var inDegree = nodeIds.ToDictionary(id => id, _ => 0);
        var adjacency = nodeIds.ToDictionary(id => id, _ => new List<string>());

        foreach (var conn in graph.Connections)
        {
            if (!nodeIds.Contains(conn.FromNodeInstanceId) || !nodeIds.Contains(conn.ToNodeInstanceId))
                continue;
            adjacency[conn.FromNodeInstanceId].Add(conn.ToNodeInstanceId);
            inDegree[conn.ToNodeInstanceId]++;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var sorted = new List<string>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            sorted.Add(node);
            foreach (var next in adjacency[node])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        if (sorted.Count != nodeIds.Count)
            return (sorted, "Cyclic dependency detected in graph.");

        return (sorted, null);
    }

    /// <summary>Fragment 間の依存関係に基づくトポロジカルソート。</summary>
    public static (List<string> sorted, string? error) TopologicalSortFragments(NodeGraph graph)
    {
        var fragIds = graph.Fragments.Select(f => f.Id).ToHashSet();
        var inDegree = fragIds.ToDictionary(id => id, _ => 0);
        var adjacency = fragIds.ToDictionary(id => id, _ => new List<string>());

        var nodeToFrag = new Dictionary<string, string>();
        foreach (var frag in graph.Fragments)
            foreach (var nid in frag.NodeInstanceIds)
                nodeToFrag[nid] = frag.Id;

        foreach (var fl in graph.FragmentLinks)
        {
            var fromFrag = nodeToFrag.TryGetValue(fl.SourceSnapshotNodeInstanceId, out var ff) ? ff : null;
            var toFrag = nodeToFrag.TryGetValue(fl.ToNodeInstanceId, out var tf) ? tf : null;
            if (fromFrag == null || toFrag == null || fromFrag == toFrag) continue;

            adjacency[fromFrag].Add(toFrag);
            inDegree[toFrag]++;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var sorted = new List<string>();
        while (queue.Count > 0)
        {
            var fid = queue.Dequeue();
            sorted.Add(fid);
            foreach (var next in adjacency[fid])
            {
                inDegree[next]--;
                if (inDegree[next] == 0) queue.Enqueue(next);
            }
        }

        if (sorted.Count != fragIds.Count)
            return (sorted, "Cyclic dependency detected between fragments.");

        return (sorted, null);
    }

    /// <summary>
    /// 通常コネクタ（FragmentLink 除く）の連結成分から断片定義を自動導出する。
    /// Union-Find でノードを連結成分に分類する。
    /// 断片 ID は構成ノードの最小 InstanceId から決まるため安定している。
    /// </summary>
    public static IReadOnlyList<FragmentDefinition> ComputeFragmentsFromConnections(NodeGraph graph)
    {
        if (graph.Nodes.Count == 0) return Array.Empty<FragmentDefinition>();

        var parent = new Dictionary<string, string>();

        string Find(string x)
        {
            if (!parent.ContainsKey(x)) parent[x] = x;
            if (parent[x] != x) parent[x] = Find(parent[x]);
            return parent[x];
        }

        void Union(string x, string y)
        {
            var rx = Find(x);
            var ry = Find(y);
            if (rx == ry) return;
            if (string.Compare(rx, ry, StringComparison.Ordinal) < 0)
                parent[ry] = rx;
            else
                parent[rx] = ry;
        }

        foreach (var node in graph.Nodes) Find(node.InstanceId);
        foreach (var conn in graph.Connections) Union(conn.FromNodeInstanceId, conn.ToNodeInstanceId);

        var components = new Dictionary<string, List<string>>();
        foreach (var node in graph.Nodes)
        {
            var root = Find(node.InstanceId);
            if (!components.ContainsKey(root)) components[root] = new List<string>();
            components[root].Add(node.InstanceId);
        }

        return components.Keys
            .OrderBy(r => r, StringComparer.Ordinal)
            .Select((root, i) => new FragmentDefinition
            {
                Id = $"auto-{root}",
                Name = $"Fragment {i + 1}",
                NodeInstanceIds = components[root],
            })
            .ToList();
    }
}
