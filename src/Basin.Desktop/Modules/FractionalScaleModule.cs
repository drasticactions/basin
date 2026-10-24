using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_fractional_scale_manager_v1</c> global through a <see cref="FractionalScaleManager"/>.
/// </summary>
public sealed class FractionalScaleModule : DesktopModule<FractionalScaleManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_fractional_scale_manager_v1";

    /// <inheritdoc/>
    public override int Version => FractionalScaleManager.Version;

    /// <inheritdoc/>
    protected override FractionalScaleManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<OutputLayout>());
}
