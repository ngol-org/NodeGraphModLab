using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using NodeGraphModLab;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.NodeAPI;
using NodeGraphModLab.Server;

namespace NodeGraphModLab.Tests;

/// <summary>
/// <c>.rsp</c> の <c>/analyzer:</c> 経由で Roslyn Source Generator（<see cref="IIncrementalGenerator"/>）
/// を実行する機能の単体テスト。実ゲームを介した手動検証（8回以上の再起動を要した）を
/// 今後は <c>dotnet test</c> で秒単位で再現できるようにするための回帰テスト。
///
/// テスト用の Generator DLL は本テストの実行時に Roslyn 自身でその場コンパイルする（テストプロセスに
/// 既にロード済みの Microsoft.CodeAnalysis(.CSharp) を参照するため、
/// 「Analyzer DLL はホストと同一 Roslyn バージョンでなければならない」制約を自動的に満たす）。
/// </summary>
[TestFixture]
public class RoslynCompilerSourceGeneratorTests
{
    private sealed class NullNgolLogger : INgolLogger
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
        public void LogDebug(string message) { }
    }

    private sealed class RecordingLogger : INgolLogger
    {
        public List<(string Level, string Message)> Calls = new();
        public void LogInfo(string message) => Calls.Add(("INFO", message));
        public void LogWarning(string message) => Calls.Add(("WARN", message));
        public void LogError(string message) => Calls.Add(("ERROR", message));
        public void LogDebug(string message) => Calls.Add(("DEBUG", message));
    }

    private const string GeneratorSource = """
        using System.Linq;
        using System.Text;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using Microsoft.CodeAnalysis.Text;

        namespace TestGen;

        [Generator(LanguageNames.CSharp)]
        public sealed class TestGreetGenerator : IIncrementalGenerator
        {
            public void Initialize(IncrementalGeneratorInitializationContext context)
            {
                context.RegisterPostInitializationOutput(static ctx => ctx.AddSource(
                    "TestGreetAttribute.g.cs",
                    SourceText.From("[System.AttributeUsage(System.AttributeTargets.Class)] internal sealed class TestGreetAttribute : System.Attribute { }", Encoding.UTF8)));

                var classes = context.SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cds
                        && cds.AttributeLists.SelectMany(al => al.Attributes)
                            .Any(a => a.Name.ToString() is "TestGreet" or "TestGreetAttribute"),
                    transform: static (ctx, _) => ((ClassDeclarationSyntax)ctx.Node).Identifier.Text);

                context.RegisterSourceOutput(classes, static (spc, className) =>
                {
                    var src = "partial class " + className + " { public string SayHello_Generated() => \"hello-from-test-generator\"; }";
                    spc.AddSource(className + ".g.cs", SourceText.From(src, Encoding.UTF8));
                });
            }
        }
        """;

    private const string NodeSourceTemplate = """
        using NodeGraphModLab.NodeAPI;

        [NodeType("{0}", "Test", "Greet Regression Test")]
        [NodePort("greeting", PortDirection.Output, "string")]
        [TestGreet]
        public sealed partial class {1} : INode
        {{
            public void Execute(IExecutionContext ctx)
            {{
                ctx.SetPortValue("greeting", SayHello_Generated());
            }}
        }}
        """;

    /// <summary>
    /// テスト対象プロセスに既にロード済みの参照アセンブリ（TRUSTED_PLATFORM_ASSEMBLIES）+
    /// Microsoft.CodeAnalysis(.CSharp) を使って Generator ソースをその場コンパイルし、DLL として保存する。
    /// アセンブリ名はテストごとに一意にすること — .NET の Assembly.LoadFrom は「同一プロセス内で
    /// 同名アセンブリを別パスから再ロードできない」ため（RunSourceGenerators の AnalyzerAssemblyCache は
    /// パスキーだが、この制約自体は.NETランタイム側でパスに関わらず働く。同一テストプロセス内で
    /// 複数の異なる Analyzer DLL を扱う際の既知の注意点）。
    /// </summary>
    private static void CompileGeneratorDll(string outputPath, string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(GeneratorSource);
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        var references = trustedAssemblies
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(IIncrementalGenerator).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var emitResult = compilation.Emit(outputPath);
        Assert.That(emitResult.Success, Is.True,
            "Test generator itself failed to compile: " + string.Join("; ", emitResult.Diagnostics));
    }

    private static string WriteTempFile(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ngol-gentest-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Analyzer DLL は RunSourceGenerators() の静的キャッシュ経由で Assembly.LoadFrom され、
    /// テストプロセスの生存期間中ロックされ続ける（同種の Assembly.LoadFrom キャッシュ挙動）。
    /// クリーンアップの削除失敗でアサーション結果がマスクされないよう握りつぶす。
    /// </summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* locked by AnalyzerAssemblyCache; ignore */ }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* ignore */ }
    }

    [Test]
    public async Task CompileAndRegisterAsync_WithAnalyzerDirective_GeneratedMethodIsCallable()
    {
        var generatorDllPath = Path.Combine(Path.GetTempPath(), $"ngol-gentest-{Guid.NewGuid():N}.dll");
        var rspPath = WriteTempFile($"/analyzer:{generatorDllPath}", ".rsp");
        const string className = "GreetRegressionTestNode1";
        const string nodeTypeId = "test.t0182regression.greet1";
        try
        {
            CompileGeneratorDll(generatorDllPath, "TestGreetGenerator1");

            var registry = new NodeRegistry();
            var response = await RoslynCompiler.CompileAndRegisterAsync(
                string.Format(NodeSourceTemplate, nodeTypeId, className),
                className, registry, new NullNgolLogger(), rspFilePath: rspPath);

            Assert.That(response.Success, Is.True,
                "Compile should succeed with the generator supplying SayHello_Generated(): " + response.ErrorMessage);

            var instance = registry.CreateInstance(nodeTypeId);
            Assert.That(instance, Is.Not.Null);

            var ctx = new TestExecutionContext("test-instance");
            instance!.Execute(ctx);

            Assert.That(ctx.GetOutput("greeting"), Is.EqualTo("hello-from-test-generator"));
        }
        finally
        {
            TryDelete(generatorDllPath);
            TryDelete(rspPath);
        }
    }

    [Test]
    public async Task CompileAndRegisterAsync_WithoutAnalyzerDirective_FailsBecauseGeneratedMethodIsMissing()
    {
        // 陰性対照実験（実機検証と同じ発想）: /analyzer: を指定しなければ
        // SayHello_Generated() は存在せず、コンパイルは失敗するはず。
        var rspPath = WriteTempFile("# no /analyzer: directive", ".rsp");
        const string className = "GreetRegressionTestNode2";
        const string nodeTypeId = "test.t0182regression.greet2";
        try
        {
            var registry = new NodeRegistry();
            var response = await RoslynCompiler.CompileAndRegisterAsync(
                string.Format(NodeSourceTemplate, nodeTypeId, className),
                className, registry, new NullNgolLogger(), rspFilePath: rspPath);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("Compilation failed"));
        }
        finally
        {
            TryDelete(rspPath);
        }
    }

    [Test]
    public async Task CompileMultipleAndSaveAsync_WithAnalyzerDirective_ProducesLoadableDll()
    {
        // CompileAndRegister（ホットリロード経路）だけでなく
        // CompileMultipleAndSave（DLLパック配布経路）にも Generator 実行を組み込んだため、
        // そちらも独立に確認する。
        var generatorDllPath = Path.Combine(Path.GetTempPath(), $"ngol-gentest-{Guid.NewGuid():N}.dll");
        var rspPath = WriteTempFile($"/analyzer:{generatorDllPath}", ".rsp");
        var outputDir = Path.Combine(Path.GetTempPath(), $"ngol-gentest-out-{Guid.NewGuid():N}");
        const string className = "GreetRegressionTestNode3";
        const string nodeTypeId = "test.t0182regression.greet3";
        try
        {
            CompileGeneratorDll(generatorDllPath, "TestGreetGenerator3");
            Assert.That(File.Exists(generatorDllPath), Is.True, "sanity: generator dll must exist before compiling");
            Assert.That(File.ReadAllText(rspPath), Does.Contain("/analyzer:"), "sanity: rsp must contain /analyzer:");

            var recordingLog = new RecordingLogger();
            var (success, savedPath, errorMessage) = await RoslynCompiler.CompileMultipleAndSaveAsync(
                new[] { (string.Format(NodeSourceTemplate, nodeTypeId, className), className) },
                assemblyName: "T0182RegressionPack",
                outputDir: outputDir,
                log: recordingLog,
                rspFilePaths: new[] { rspPath });

            Assert.That(success, Is.True, "CompileMultipleAndSave should succeed: " + errorMessage
                + " | log: " + string.Join(" || ", recordingLog.Calls.Select(c => $"[{c.Level}] {c.Message}")));
            Assert.That(savedPath, Is.Not.Null);
            Assert.That(File.Exists(savedPath!), Is.True);

            var asm = Assembly.LoadFrom(savedPath!);
            var type = asm.GetType(className);
            Assert.That(type, Is.Not.Null, "Compiled DLL should contain the generated partial method's owning type");
            Assert.That(type!.GetMethod("SayHello_Generated"), Is.Not.Null);
        }
        finally
        {
            TryDelete(generatorDllPath);
            TryDelete(rspPath);
            TryDeleteDir(outputDir);
        }
    }
}
