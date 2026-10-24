namespace Basin.Shell.Weston;

/// <summary>
/// Installs <c>weston_screensaver</c>. It requires an <see cref="IShellRoles"/> driver.
/// </summary>
public sealed class WestonScreensaverModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "weston_screensaver";

    /// <inheritdoc/>
    public int Version => WestonScreensaverGlobal.Version;

    /// <inheritdoc/>
    public IReadOnlyList<Type> Drivers => [typeof(IShellRoles)];

    /// <summary>
    /// Gets the global that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public WestonScreensaverGlobal? Global { get; private set; }

    /// <inheritdoc/>
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
