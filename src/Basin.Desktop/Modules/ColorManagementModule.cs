using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ColorManagementModule : DesktopModule<ColorManager>
{
    public override string WireInterface => "wp_color_manager_v1";

    public override int Version => ColorManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IColorProfileService)];

    public override IReadOnlyList<Type> Drivers => [typeof(IColorTransformResolver)];

    protected override ColorManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>())
        {
            Profiles = services.Find<IColorProfileService>(),
            Resolver = services.Require<IColorTransformResolver>(),
        };
}
