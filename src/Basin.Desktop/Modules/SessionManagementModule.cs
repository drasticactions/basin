using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>xdg_session_manager_v1</c> global through a <see cref="SessionManager"/>. It keeps window state in <see cref="ISessionStore"/>.
/// </summary>
public sealed class SessionManagementModule : DesktopModule<SessionManager>
{
    private readonly string? _appName;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionManagementModule"/> class.
    /// </summary>
    /// <param name="appName">The application name for a <see cref="FileSessionStore"/> default. When it is <see langword="null"/>, the module seeds no <see cref="ISessionStore"/>.</param>
    public SessionManagementModule(string? appName = null) => _appName = appName;

    /// <inheritdoc/>
    public override string WireInterface => "xdg_session_manager_v1";

    /// <inheritdoc/>
    public override int Version => SessionManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(ISessionStore)];

    /// <inheritdoc/>
    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (_appName is { } app && services.Find<ISessionStore>() is null)
        {
            services.UseDefault<ISessionStore>(new FileSessionStore(app));
        }
    }

    /// <inheritdoc/>
    protected override SessionManager Create(BasinServices services) =>
        new(services.Display, services.Find<ISessionStore>());
}
