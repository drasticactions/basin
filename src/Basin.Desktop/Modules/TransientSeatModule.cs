using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>ext_transient_seat_manager_v1</c> global through a <see cref="TransientSeatManager"/>.
/// </summary>
public sealed class TransientSeatModule : DesktopModule<TransientSeatManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "ext_transient_seat_manager_v1";

    /// <inheritdoc/>
    public override int Version => TransientSeatManager.Version;

    /// <inheritdoc/>
    protected override TransientSeatManager Create(BasinServices services) =>
        new(services.Display, services.Find<CompositorGlobal>(), services.Find<IKeymapSource>());
}
