using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class AlphaModifierModule : DesktopModule<AlphaModifierManager>
{
    public override string WireInterface => "wp_alpha_modifier_v1";

    public override int Version => AlphaModifierManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ISurfaceAppearance)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ISurfaceAppearance>(new DefaultSurfaceAppearance());
    }

    protected override AlphaModifierManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<ISurfaceAppearance>());
}
