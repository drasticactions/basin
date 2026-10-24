namespace Basin.Shell.Weston;

/// <summary>
/// Makes the protocol pack for weston's shell protocols.
/// </summary>
public static class WestonShellPack
{
    /// <summary>
    /// Makes a pack with <see cref="WestonDesktopShellModule"/> and <see cref="WestonScreensaverModule"/>. A compositor that uses it must register an <see cref="IShellRoles"/>.
    /// </summary>
    /// <param name="isPrivileged">The test that decides which client process ID can bind <c>weston_desktop_shell</c>. With <see langword="null"/>, any client can bind.</param>
    /// <returns>A new pack with both modules.</returns>
    public static ProtocolPack Create(Func<int, bool>? isPrivileged = null) =>
        new([new WestonDesktopShellModule(isPrivileged), new WestonScreensaverModule()]);
}
