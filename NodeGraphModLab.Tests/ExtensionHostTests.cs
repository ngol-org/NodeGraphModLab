using NUnit.Framework;
using NodeGraphModLab;
using NodeGraphModLab.Core.Engine;
using NodeGraphModLab.Core.Extensions;
using NodeGraphModLab.NodeAPI;

namespace NodeGraphModLab.Tests;

[TestFixture]
public class ExtensionHostTests
{
    [Test]
    public void ExtensionManifest_InvalidApiVersion_IsRejectedByHost()
    {
        using var temp = new TempExtensionDir();
        temp.WriteManifest(new
        {
            id = "test.bad.api",
            version = "1.0.0",
            apiVersion = 999,
            enabled = true,
            entryAssembly = "Missing.dll",
            entryType = "Missing.Type",
        });

        var host = new ExtensionHost(new NullNgolLogger());
        host.LoadAll(temp.NgolRoot, new NodeRegistry(), new PersistentNodeRunner());

        Assert.That(host.Capabilities, Is.Empty);
    }

    [Test]
    public void ExtensionManifest_Disabled_IsSkipped()
    {
        using var temp = new TempExtensionDir();
        temp.WriteManifest(new
        {
            id = "test.disabled",
            version = "1.0.0",
            apiVersion = 1,
            enabled = false,
            entryAssembly = "Missing.dll",
            entryType = "Missing.Type",
        });

        var host = new ExtensionHost(new NullNgolLogger());
        host.LoadAll(temp.NgolRoot, new NodeRegistry(), new PersistentNodeRunner());

        Assert.That(host.Capabilities, Is.Empty);
    }

    [Test]
    public void ExtensionServiceRegistry_RegisterAndGet_RoundTrip()
    {
        var registry = new ExtensionServiceRegistry();
        var service = new TestService { Value = 42 };
        registry.Register("ext.test", typeof(ITestService), service, ExtensionServiceScope.Extension);

        Assert.That(registry.GetService<ITestService>()?.Value, Is.EqualTo(42));
    }

    [Test]
    public void ExtensionServiceRegistry_RemoveExtension_ClearsExtensionScopedServices()
    {
        var registry = new ExtensionServiceRegistry();
        registry.Register("ext.a", typeof(ITestService), new TestService { Value = 1 }, ExtensionServiceScope.Extension);
        registry.Register("ext.b", typeof(ITestService), new TestService { Value = 2 }, ExtensionServiceScope.Singleton);

        registry.RemoveExtension("ext.a");

        Assert.That(registry.GetService<ITestService>()?.Value, Is.EqualTo(2));
    }

    [Test]
    public void GetExtensionService_DelegatesFromInlineExecutionContext()
    {
        var registry = new ExtensionServiceRegistry();
        registry.Register("ext.test", typeof(ITestService), new TestService { Value = 7 }, ExtensionServiceScope.Extension);

        var parent = new MainThreadExecutionContext(
            "parent", new NullNgolLogger(), new PersistentNodeRunner(), extensionServices: registry);
        var ctx = new InlineExecutionContext(
            "node-1",
            new Dictionary<string, System.Text.Json.JsonElement>(),
            new Dictionary<string, object?>(),
            parent);

        Assert.That(ctx.GetExtensionService<ITestService>()?.Value, Is.EqualTo(7));
        Assert.That(ctx.GetExtensionService<IMissingService>(), Is.Null);
    }

    [Test]
    public void ExtensionHost_LoadsTestExtension_FromCurrentAssembly()
    {
        using var temp = new TempExtensionDir();
        var asmName = typeof(TestNgolExtension).Assembly.GetName().Name + ".dll";
        var asmPath = typeof(TestNgolExtension).Assembly.Location;
        File.Copy(asmPath, Path.Combine(temp.ExtensionDir, asmName), overwrite: true);

        temp.WriteManifest(new
        {
            id = "test.ngol.extension",
            version = "1.0.0",
            apiVersion = 1,
            enabled = true,
            entryAssembly = asmName,
            entryType = typeof(TestNgolExtension).FullName,
            capabilities = new[] { "test.capability" },
        });

        var host = new ExtensionHost(new NullNgolLogger());
        var runner = new PersistentNodeRunner();
        host.LoadAll(temp.NgolRoot, new NodeRegistry(), runner);

        Assert.That(TestNgolExtension.Loaded, Is.True);
        Assert.That(host.Capabilities.Any(c => c.CapabilityId == "test.capability"), Is.True);
        Assert.That(host.ServiceRegistry.GetService<ITestService>()?.Value, Is.EqualTo(99));

        host.UnloadAll();
        Assert.That(TestNgolExtension.Loaded, Is.False);
        Assert.That(host.ServiceRegistry.GetService<ITestService>(), Is.Null);
    }

    public interface ITestService
    {
        int Value { get; }
    }

    public interface IMissingService { }

    private sealed class TestService : ITestService
    {
        public int Value { get; init; }
    }

    public sealed class TestNgolExtension : INgolExtension
    {
        public static bool Loaded;

        public void Load(IExtensionContext context)
        {
            Loaded = true;
            context.RegisterService(typeof(ITestService), new TestService { Value = 99 }, ExtensionServiceScope.Extension);
            context.RegisterCapability("test.capability", "1.0.0");
        }

        public void Unload(IExtensionContext context)
        {
            Loaded = false;
        }
    }

    private sealed class TempExtensionDir : IDisposable
    {
        public string NgolRoot { get; }
        public string ExtensionDir { get; }

        public TempExtensionDir()
        {
            NgolRoot = Path.Combine(Path.GetTempPath(), "ngol-ext-test-" + Guid.NewGuid().ToString("N"));
            ExtensionDir = Path.Combine(NgolRoot, "Extensions", "test.ext");
            Directory.CreateDirectory(ExtensionDir);
        }

        public void WriteManifest(object manifest)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(manifest);
            File.WriteAllText(Path.Combine(ExtensionDir, "extension.json"), json);
        }

        public void Dispose()
        {
            try { Directory.Delete(NgolRoot, recursive: true); } catch { }
        }
    }

    private sealed class NullNgolLogger : INgolLogger
    {
        public void LogInfo(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
        public void LogDebug(string message) { }
    }
}
