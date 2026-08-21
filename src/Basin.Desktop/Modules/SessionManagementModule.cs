using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class SessionManagementModule : DesktopModule<SessionManager>
{
    private readonly string? _appName;

    public SessionManagementModule(string? appName = null) => _appName = appName;

    public override string WireInterface => "xdg_session_manager_v1";

    public override int Version => SessionManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ISessionStore)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (_appName is { } app && services.Find<ISessionStore>() is null)
        {
            services.UseDefault<ISessionStore>(new FileSessionStore(app));
        }
    }

    protected override SessionManager Create(BasinServices services) =>
        new(services.Display, services.Find<ISessionStore>());
}
