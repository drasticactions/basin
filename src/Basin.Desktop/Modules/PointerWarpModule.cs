using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_pointer_warp_v1</c> global through a <see cref="PointerWarpManager"/>.
/// </summary>
public sealed class PointerWarpModule : DesktopModule<PointerWarpManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_pointer_warp_v1";

    /// <inheritdoc/>
    public override int Version => PointerWarpManager.Version;

    /// <inheritdoc/>
    protected override PointerWarpManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<Basin.Seat.Seat>());
}
