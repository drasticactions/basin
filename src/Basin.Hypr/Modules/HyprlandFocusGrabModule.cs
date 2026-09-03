using Basin.Desktop;

namespace Basin.Hypr;

public sealed class HyprlandFocusGrabModule : DesktopModule<HyprlandFocusGrabManager>
{
    public override string WireInterface => "hyprland_focus_grab_manager_v1";

    public override int Version => HyprlandFocusGrabManager.Version;

    public override IReadOnlyList<Type> Drivers => [typeof(Basin.Seat.Seat)];

    protected override HyprlandFocusGrabManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<Basin.Seat.Seat>());
}
