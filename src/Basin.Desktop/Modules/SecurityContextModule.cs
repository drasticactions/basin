using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class SecurityContextModule : DesktopModule<SecurityContextManager>
{
    public override string WireInterface => "wp_security_context_manager_v1";

    public override int Version => SecurityContextManager.Version;

    protected override SecurityContextManager Create(BasinServices services) =>
        new(services.Display, services.Loop);
}
