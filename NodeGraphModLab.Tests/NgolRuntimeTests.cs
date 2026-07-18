using NUnit.Framework;
using NodeGraphModLab;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class NgolRuntimeTests
{
    [Test]
    public void ShouldSkipStandaloneCompile_NoNodeTypeAttribute_ReturnsTrue()
    {
        const string source = "internal static class Helper { public static int Add(int a, int b) => a + b; }";

        Assert.That(NgolRuntime.ShouldSkipStandaloneCompile(source, hasDependents: false), Is.True);
    }

    [Test]
    public void ShouldSkipStandaloneCompile_HasNodeTypeAttribute_NotSharedFile_ReturnsFalse()
    {
        const string source = "[NodeType(\"custom.example\")] public class ExampleNode : INode { }";

        Assert.That(NgolRuntime.ShouldSkipStandaloneCompile(source, hasDependents: false), Is.False);
    }

    [Test]
    public void ShouldSkipStandaloneCompile_HasNodeTypeAttribute_ButKnownSharedFile_ReturnsTrue()
    {
        // NodeType文字列を含んでいても、他ノードの.srclistに列挙済みなら保険的にスキップする
        const string source = "[NodeType(\"custom.example\")] public class ExampleNode : INode { }";

        Assert.That(NgolRuntime.ShouldSkipStandaloneCompile(source, hasDependents: true), Is.True);
    }

    [Test]
    public void ShouldSkipStandaloneCompile_NoNodeTypeAttribute_AndSharedFile_ReturnsTrue()
    {
        const string source = "internal static class VrmExportHelpers { }";

        Assert.That(NgolRuntime.ShouldSkipStandaloneCompile(source, hasDependents: true), Is.True);
    }
}
