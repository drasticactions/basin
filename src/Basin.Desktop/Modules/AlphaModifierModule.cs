using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_alpha_modifier_v1</c> global through a <see cref="AlphaModifierManager"/>. It seeds a <see cref="DefaultSurfaceAppearance"/> as the <see cref="ISurfaceAppearance"/> unless the consumer registers one.
/// </summary>
public sealed class AlphaModifierModule : DesktopModule<AlphaModifierManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_alpha_modifier_v1";

    /// <inheritdoc/>
    public override int Version => AlphaModifierManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(ISurfaceAppearance)];

    /// <inheritdoc/>
    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ISurfaceAppearance>(new DefaultSurfaceAppearance());
    }

    /// <inheritdoc/>
    protected override AlphaModifierManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<ISurfaceAppearance>());
}
