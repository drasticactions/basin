using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_tearing_control_manager_v1</c> global through a <see cref="TearingControlManager"/>.
/// </summary>
public sealed class TearingControlModule : DesktopModule<TearingControlManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_tearing_control_manager_v1";

    /// <inheritdoc/>
    public override int Version => TearingControlManager.Version;

    /// <inheritdoc/>
    protected override TearingControlManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<ISurfaceHints>() as SurfaceHints);
}
