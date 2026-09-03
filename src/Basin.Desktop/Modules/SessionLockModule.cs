using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class SessionLockModule : DesktopModule<SessionLockManager>
{
    private readonly SessionLockState _state = new();

    public override string WireInterface => "ext_session_lock_manager_v1";

    public override int Version => SessionLockManager.Version;

    public SessionLockState State => _state;

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ILockState>(_state);
    }

    protected override SessionLockManager Create(BasinServices services)
    {
        var manager = new SessionLockManager(
            services.Display, services.Require<CompositorGlobal>(), services.Find<OutputLayout>());
        _state.Attach(manager);
        return manager;
    }
}
