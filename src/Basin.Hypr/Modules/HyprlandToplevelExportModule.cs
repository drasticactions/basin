using Basin.Capabilities;
using Basin.Desktop;

namespace Basin.Hypr;

public sealed class HyprlandToplevelExportModule : DesktopModule<HyprlandToplevelExportManager>
{
    public override string WireInterface => "hyprland_toplevel_export_manager_v1";

    public override int Version => HyprlandToplevelExportManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IScreenCapture), typeof(IToplevelModel)];

    protected override HyprlandToplevelExportManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<OutputLayout>(),
            services.Require<ClientBufferRegistry>(),
            services.Find<IScreenCapture>(),
            services.Find<IToplevelModel>(),
            services.Find<ICaptureDmabufConstraints>());
}
