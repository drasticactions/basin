using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;

namespace Basin.Hypr;

public sealed class HyprlandSurfaceModule : DesktopModule<HyprlandSurfaceManager>
{
    public override string WireInterface => "hyprland_surface_manager_v1";

    public override int Version => HyprlandSurfaceManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ISurfaceAppearance)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ISurfaceAppearance>(new DefaultSurfaceAppearance());
    }

    protected override HyprlandSurfaceManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<ISurfaceAppearance>());
}
