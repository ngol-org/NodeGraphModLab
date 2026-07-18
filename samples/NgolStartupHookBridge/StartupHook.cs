// .NETランタイムの DOTNET_STARTUP_HOOKS 規約により、この型は名前空間を持てず、
// 型名は StartupHook、シグネチャは public static void Initialize() 固定。
internal static class StartupHook
{
    public static void Initialize()
    {
        try
        {
            NgolStartupHookBridge.NgolActivator.TryStart();
        }
        catch
        {
            // Initialize() から例外を投げるとホストプロセスの起動自体が失敗するため、
            // ここで確実に握りつぶす。詳細は NgolActivator が書き出す
            // startup-hook-bridge.log / host.log を参照。
        }
    }
}
