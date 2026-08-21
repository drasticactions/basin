using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class SessionLockModule : DesktopModule<SessionLockManager>
{
    public override string WireInterface => "ext_session_lock_manager_v1";

    public override int Version => SessionLockManager.Version;

    protected override SessionLockManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
