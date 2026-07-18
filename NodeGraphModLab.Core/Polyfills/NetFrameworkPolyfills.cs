// .NET Framework 4.x ビルド向けポリフィル
// net462 ターゲットで不足する BCL API を補完する拡張メソッド群

#if !NET5_0_OR_GREATER
#pragma warning disable CS0436

namespace System.Collections.Generic
{
    internal static class NetFrameworkCollectionPolyfills
    {
        /// <summary>IEnumerable&lt;T&gt;.ToHashSet() — .NET Standard 2.0 / .NET Framework 4.x 用ポリフィル</summary>
        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source)
            => new HashSet<T>(source);

        /// <summary>KeyValuePair&lt;K,V&gt; のタプル分解 — .NET Framework 4.x 用ポリフィル</summary>
        public static void Deconstruct<K, V>(this KeyValuePair<K, V> pair, out K key, out V value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}

namespace System
{
    internal static class NetFrameworkStringPolyfills
    {
        /// <summary>string.Contains(string, StringComparison) — .NET Standard 2.1+ 用ポリフィル</summary>
        public static bool Contains(this string str, string value, StringComparison comparison)
            => str.IndexOf(value, comparison) >= 0;
    }
}

#pragma warning restore CS0436
#endif
