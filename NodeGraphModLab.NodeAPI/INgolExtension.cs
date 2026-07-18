namespace NodeGraphModLab.NodeAPI;

/// <summary>
/// NGOL Extension のエントリポイント。Impl DLL が実装する。
/// </summary>
public interface INgolExtension
{
    void Load(IExtensionContext context);
    void Unload(IExtensionContext context);
}
