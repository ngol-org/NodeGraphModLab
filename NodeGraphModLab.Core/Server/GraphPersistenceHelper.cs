using System.Collections.Generic;
using System.IO;
using System.Text;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

internal static class GraphPersistenceHelper
{
    public static bool TrySave(NodeGraph graph, string saveDir, INgolLogger? log)
    {
        try
        {
            var path = GetGraphPath(graph.Id, saveDir);
            File.WriteAllText(path, graph.ToJson(), Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            log?.LogError($"[GraphServer] SaveGraph error: {ex.Message}");
            return false;
        }
    }

    public static NodeGraph? TryLoad(string id, string saveDir)
    {
        try
        {
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            var path = GetGraphPath(id, saveDir);
            if (!File.Exists(path)) return null;
            return NodeGraph.FromJson(File.ReadAllText(path, Encoding.UTF8));
        }
        catch { return null; }
    }

    public static bool TryDelete(string id, string saveDir)
    {
        try
        {
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            var path = GetGraphPath(id, saveDir);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    public static List<GraphSummary> ListSummaries(string saveDir)
    {
        var result = new List<GraphSummary>();
        if (!Directory.Exists(saveDir)) return result;
        foreach (var file in Directory.EnumerateFiles(saveDir, "*.json"))
        {
            try
            {
                var graph = NodeGraph.FromJson(File.ReadAllText(file, Encoding.UTF8));
                if (graph != null)
                    result.Add(new GraphSummary { Id = graph.Id, Name = graph.Name, Description = graph.Description });
            }
            catch { /* 破損ファイルはスキップ */ }
        }
        return result;
    }

    public static string GetGraphPath(string id, string saveDir) => Path.Combine(saveDir, id + ".json");
}
