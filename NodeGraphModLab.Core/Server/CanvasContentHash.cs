using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Server;

/// <summary>
/// キャンバス内容の同一性を表す短い文字列を作る。
///
/// 用途は「前回返したものと同じか」の判定だけなので暗号強度は要らない。
/// ただし誤って「同じ」と判定すると要求元が編集を取りこぼすため、32bit 1本では足りない。
/// 独立した 2 本と長さを組み合わせる。
/// </summary>
internal static class CanvasContentHash
{
    /// <summary>
    /// 受け取ったグラフを <see cref="NodeGraph"/> へ入れ直したうえで直列化するため、
    /// ブラウザ側の出力ゆらぎ（空白・キー順）に左右されない。
    /// </summary>
    public static string Compute(NodeGraph graph)
    {
        // 組み立て時刻は編集していなくても呼ぶたびに変わる。含めると常に「変化あり」になる。
        var saved = graph.CreatedAt;
        graph.CreatedAt = default;
        string json;
        try { json = graph.ToJson(); }
        finally { graph.CreatedAt = saved; }

        uint fnv = 2166136261;
        uint djb = 5381;
        foreach (var c in json)
        {
            fnv = unchecked((fnv ^ c) * 16777619);
            djb = unchecked(djb * 33 ^ c);
        }
        return Base36(unchecked((uint)json.Length)) + "-" + Base36(fnv) + "-" + Base36(djb);
    }

    private static string Base36(uint value)
    {
        if (value == 0) return "0";
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var buf = new char[7];
        var i = buf.Length;
        while (value > 0)
        {
            buf[--i] = digits[(int)(value % 36)];
            value /= 36;
        }
        return new string(buf, i, buf.Length - i);
    }
}
