using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_relative_pointer_manager_v1</c> global through a <see cref="RelativePointerManager"/>.
/// </summary>
public sealed class RelativePointerModule : DesktopModule<RelativePointerManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_relative_pointer_manager_v1";

    /// <inheritdoc/>
    public override int Version => RelativePointerManager.Version;

    /// <inheritdoc/>
    protected override RelativePointerManager Create(BasinServices services) =>
        new(services.Display, services.Require<Seat.Seat>());
}
