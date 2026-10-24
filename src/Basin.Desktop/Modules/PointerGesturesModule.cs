using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_pointer_gestures_v1</c> global through a <see cref="PointerGesturesManager"/>.
/// </summary>
public sealed class PointerGesturesModule : DesktopModule<PointerGesturesManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_pointer_gestures_v1";

    /// <inheritdoc/>
    public override int Version => PointerGesturesManager.Version;

    /// <inheritdoc/>
    protected override PointerGesturesManager Create(BasinServices services) =>
        new(services.Display, services.Require<Seat.Seat>());
}
