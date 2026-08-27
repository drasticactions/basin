using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class FullscreenShellModule : DesktopModule<FullscreenShellGlobal>
{
    public override string WireInterface => "zwp_fullscreen_shell_v1";

    public override int Version => FullscreenShellGlobal.Version;

    protected override FullscreenShellGlobal Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<OutputLayout>());
}
