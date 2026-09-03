using Basin.Capabilities;
using Basin.Desktop;

namespace Basin.Hypr;

public sealed class HyprlandCtmModule : DesktopModule<HyprlandCtmControlManager>
{
    public override string WireInterface => "hyprland_ctm_control_manager_v1";

    public override int Version => HyprlandCtmControlManager.Version;

    public override IReadOnlyList<Type> Drivers => [typeof(ICtmControl)];

    protected override HyprlandCtmControlManager Create(BasinServices services) =>
        new(services.Display, services.Require<OutputLayout>(), services.Require<ICtmControl>());
}
