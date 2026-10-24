using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>ext_background_effect_manager_v1</c> global through a <see cref="BackgroundEffectManager"/>.
/// </summary>
public sealed class BackgroundEffectModule : DesktopModule<BackgroundEffectManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "ext_background_effect_manager_v1";

    /// <inheritdoc/>
    public override int Version => BackgroundEffectManager.Version;

    /// <inheritdoc/>
    protected override BackgroundEffectManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<IBackgroundEffects>());
}
