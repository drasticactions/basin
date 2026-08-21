namespace Basin.Shell.Weston;

public sealed class WestonDesktopShellModule : IProtocolModule
{
    private readonly Func<int, bool>? _isPrivileged;

    public WestonDesktopShellModule(Func<int, bool>? isPrivileged = null) => _isPrivileged = isPrivileged;

    public string WireInterface => "weston_desktop_shell";

    public int Version => WestonDesktopShellGlobal.Version;

    public IReadOnlyList<Type> Drivers => [typeof(IShellRoles)];

    public WestonDesktopShellGlobal? Global { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Global = new WestonDesktopShellGlobal(
            services.Display,
            services.Require<CompositorGlobal>(),
            services.Require<IShellRoles>(),
            _isPrivileged);
        services.Use(Global);
        services.Use<IShellClient>(Global);
        return Global;
    }
}
