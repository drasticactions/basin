using Basin.Capabilities;
using Basin.Desktop;

namespace Basin.Hypr;

public sealed class HyprlandToplevelMappingModule : DesktopModule<HyprlandToplevelMappingManager>
{
    public override string WireInterface => "hyprland_toplevel_mapping_manager_v1";

    public override int Version => HyprlandToplevelMappingManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel)];

    protected override HyprlandToplevelMappingManager Create(BasinServices services) =>
        new(services.Display, services.Find<IToplevelModel>());
}
