using System.Collections.Generic;
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

    // ---- ExtractNodeTypeIds ----

    [Test]
    public void ExtractNodeTypeIds_SingleAttribute_ReturnsId()
    {
        const string source = "[NodeType(\"custom.example\", \"Category\", \"Example\")] public class ExampleNode : INode { }";

        Assert.That(NgolRuntime.ExtractNodeTypeIds(source), Is.EqualTo(new[] { "custom.example" }));
    }

    [Test]
    public void ExtractNodeTypeIds_MultipleAttributes_ReturnsAllInDeclarationOrder()
    {
        const string source = """
            [NodeType("custom.b", "Category", "B")] public class BNode : INode { }
            [NodeType("custom.a", "Category", "A")] public class ANode : INode { }
            """;

        Assert.That(NgolRuntime.ExtractNodeTypeIds(source), Is.EqualTo(new[] { "custom.b", "custom.a" }));
    }

    [Test]
    public void ExtractNodeTypeIds_WithNamedArguments_ReturnsId()
    {
        const string source = "[NodeType(\"custom.example\", \"Category\", \"Example\", Version = \"1.2.0\", Description = \"desc\")]";

        Assert.That(NgolRuntime.ExtractNodeTypeIds(source), Is.EqualTo(new[] { "custom.example" }));
    }

    [Test]
    public void ExtractNodeTypeIds_WithWhitespaceAndAttributeSuffix_ReturnsId()
    {
        const string source = "[ NodeTypeAttribute ( \"custom.example\" , \"Category\" , \"Example\" ) ]";

        Assert.That(NgolRuntime.ExtractNodeTypeIds(source), Is.EqualTo(new[] { "custom.example" }));
    }

    [Test]
    public void ExtractNodeTypeIds_DuplicateIdInSameFile_ReturnedOnce()
    {
        const string source = """
            [NodeType("custom.example", "Category", "A")] public class ANode : INode { }
            [NodeType("custom.example", "Category", "B")] public class BNode : INode { }
            """;

        Assert.That(NgolRuntime.ExtractNodeTypeIds(source), Is.EqualTo(new[] { "custom.example" }));
    }

    [Test]
    public void ExtractNodeTypeIds_NoAttribute_ReturnsEmpty()
    {
        const string source = "internal static class Helper { public static int Add(int a, int b) => a + b; }";

        Assert.That(NgolRuntime.ExtractNodeTypeIds(source), Is.Empty);
    }

    [Test]
    public void ExtractNodeTypeIds_NonLiteralId_NotExtracted()
    {
        // 定数参照は抽出できない。優先コンパイル対象にならないだけで、通常のコンパイルパスで登録される
        const string source = "[NodeType(NodeIds.Example, \"Category\", \"Example\")] public class ExampleNode : INode { }";

        Assert.That(NgolRuntime.ExtractNodeTypeIds(source), Is.Empty);
    }

    // ---- BuildPriorityCompileOrder ----

    private static Dictionary<string, string> Map(params (string Id, string Path)[] entries)
    {
        var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var (id, path) in entries) map[id] = path;
        return map;
    }

    [Test]
    public void BuildPriorityCompileOrder_PriorityIdsKeepConfiguredOrder()
    {
        var map = Map(("ns.a", "A.cs"), ("ns.b", "B.cs"), ("ns.c", "C.cs"));

        var order = NgolRuntime.BuildPriorityCompileOrder(
            map, new[] { "ns.c", "ns.a" }, new string[0], out var unresolved);

        Assert.That(order, Is.EqualTo(new[] { "C.cs", "A.cs" }));
        Assert.That(unresolved, Is.Empty);
    }

    [Test]
    public void BuildPriorityCompileOrder_PriorityIdsComeBeforeStartupIds()
    {
        var map = Map(("ns.a", "A.cs"), ("ns.b", "B.cs"));

        var order = NgolRuntime.BuildPriorityCompileOrder(
            map, new[] { "ns.b" }, new[] { "ns.a" }, out _);

        Assert.That(order, Is.EqualTo(new[] { "B.cs", "A.cs" }));
    }

    [Test]
    public void BuildPriorityCompileOrder_SameFileReferencedTwice_CompiledOnce()
    {
        // 1ファイルが複数ノードを定義しているケース、および優先指定と起動対象が重複するケース
        var map = Map(("ns.a", "Multi.cs"), ("ns.b", "Multi.cs"));

        var order = NgolRuntime.BuildPriorityCompileOrder(
            map, new[] { "ns.a" }, new[] { "ns.b", "ns.a" }, out _);

        Assert.That(order, Is.EqualTo(new[] { "Multi.cs" }));
    }

    [Test]
    public void BuildPriorityCompileOrder_UnknownPriorityId_ReportedAsUnresolved()
    {
        var map = Map(("ns.a", "A.cs"));

        var order = NgolRuntime.BuildPriorityCompileOrder(
            map, new[] { "ns.typo", "ns.a" }, new string[0], out var unresolved);

        Assert.That(order, Is.EqualTo(new[] { "A.cs" }));
        Assert.That(unresolved, Is.EqualTo(new[] { "ns.typo" }));
    }

    [Test]
    public void BuildPriorityCompileOrder_UnknownStartupId_SilentlyIgnored()
    {
        // 対応する .cs が無い起動対象 ID はビルトイン/拡張 DLL 由来。既に登録済みなので警告対象にしない
        var map = Map(("ns.a", "A.cs"));

        var order = NgolRuntime.BuildPriorityCompileOrder(
            map, new string[0], new[] { "ngol.logic.log", "ns.a" }, out var unresolved);

        Assert.That(order, Is.EqualTo(new[] { "A.cs" }));
        Assert.That(unresolved, Is.Empty);
    }

    [Test]
    public void BuildPriorityCompileOrder_EmptyOrWhitespaceIds_Ignored()
    {
        var map = Map(("ns.a", "A.cs"));

        var order = NgolRuntime.BuildPriorityCompileOrder(
            map, new[] { "", "   ", " ns.a " }, new[] { "" }, out var unresolved);

        Assert.That(order, Is.EqualTo(new[] { "A.cs" }));
        Assert.That(unresolved, Is.Empty);
    }

    [Test]
    public void BuildPriorityCompileOrder_NothingConfigured_ReturnsEmpty()
    {
        var map = Map(("ns.a", "A.cs"));

        var order = NgolRuntime.BuildPriorityCompileOrder(
            map, new string[0], new string[0], out var unresolved);

        Assert.That(order, Is.Empty);
        Assert.That(unresolved, Is.Empty);
    }
}
