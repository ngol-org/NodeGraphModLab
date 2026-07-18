// IL2CPP interop 環境用 Nullable 属性ポリフィル
// System.Text.Json ソースジェネレーターと C# nullable 参照型が必要とするが
// 一部の IL2CPP interop 環境の mscorlib には含まれていないため自前定義する

#pragma warning disable CS0436 // 外部アセンブリに同名型があっても優先
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    internal sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte _) { }
        public NullableAttribute(byte[] _) { }
    }

    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Delegate |
        AttributeTargets.Interface | AttributeTargets.Method |
        AttributeTargets.Struct,
        AllowMultiple = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte _) { }
    }

#if !NET5_0_OR_GREATER
    // C# 9 init-only セッタ用ポリフィル (net462 / netstandard2.0 で必要)
    internal static class IsExternalInit { }
#endif
}
#pragma warning restore CS0436
