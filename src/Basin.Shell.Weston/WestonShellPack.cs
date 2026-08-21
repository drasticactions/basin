namespace Basin.Shell.Weston;

public static class WestonShellPack
{
    public static ProtocolPack Create(Func<int, bool>? isPrivileged = null) =>
        new([new WestonDesktopShellModule(isPrivileged), new WestonScreensaverModule()]);
}
