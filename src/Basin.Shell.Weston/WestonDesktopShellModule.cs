namespace Basin.Shell.Weston;

/// <summary>
/// Installs <c>weston_desktop_shell</c>. It requires an <see cref="IShellRoles"/> driver and registers the global as <see cref="IShellClient"/>.
/// </summary>
public sealed class WestonDesktopShellModule : IProtocolModule
{
    private readonly Func<int, bool>? _isPrivileged;

    /// <summary>
    /// Initializes a new instance of the <see cref="WestonDesktopShellModule"/> class.
    /// </summary>
    /// <param name="isPrivileged">The test that gets a client process ID and returns <see langword="true"/> when that client can bind. With <see langword="null"/>, any client can bind.</param>
    public WestonDesktopShellModule(Func<int, bool>? isPrivileged = null) => _isPrivileged = isPrivileged;

    /// <inheritdoc/>
    public string WireInterface => "weston_desktop_shell";

    /// <inheritdoc/>
    public int Version => WestonDesktopShellGlobal.Version;

    /// <inheritdoc/>
    public IReadOnlyList<Type> Drivers => [typeof(IShellRoles)];

    /// <summary>
    /// Gets the global that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public WestonDesktopShellGlobal? Global { get; private set; }

    /// <inheritdoc/>
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
