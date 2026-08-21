namespace Basin.Shell.Weston;

public sealed class WestonScreensaverModule : IProtocolModule
{
    public string WireInterface => "weston_screensaver";

    public int Version => WestonScreensaverGlobal.Version;

    public IReadOnlyList<Type> Drivers => [typeof(IShellRoles)];

    public WestonScreensaverGlobal? Global { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Global = new WestonScreensaverGlobal(
            services.Display,
            services.Require<CompositorGlobal>(),
            services.Require<IShellRoles>());
        services.Use(Global);
        return Global;
    }
}
