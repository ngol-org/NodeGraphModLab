// netstandard2.0 / .NET Framework 4.x 用 IsExternalInit ポリフィル
// C# 9 の init-only セッタが必要とするが、これらの TFM には含まれていないため自前定義する

#if !NET5_0_OR_GREATER
#pragma warning disable CS0436 // 外部アセンブリに同名型があっても優先
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#pragma warning restore CS0436
#endif
