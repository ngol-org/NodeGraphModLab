using System.Reflection;

namespace NodeGraphModLab.NodeAPI;

public enum ExtensionServiceScope
{
  Extension,
  Singleton,
}

/// <summary>
/// Extension の OnUpdate / OnStop を PersistentNodeRunner に統合するためのコールバック。
/// </summary>
public interface IExtensionPersistentWork
{
  void OnUpdate();
  void OnStop();
}

/// <summary>
/// ExtensionHost が <see cref="INgolExtension.Load"/> 時に渡す能力バンドル。
/// </summary>
public interface IExtensionContext
{
  string ExtensionId { get; }
  string NgolRoot { get; }
  string ExtensionDirectory { get; }
  INodeLogger Logger { get; }

  void RegisterNodes(Assembly assembly);
  void AddAssemblyResolvePath(string directory);
  void RegisterService(Type serviceType, object implementation, ExtensionServiceScope scope);
  void RegisterPersistentTick(IExtensionPersistentWork work);
  void RegisterCapability(string capabilityId, string? version = null);
}
