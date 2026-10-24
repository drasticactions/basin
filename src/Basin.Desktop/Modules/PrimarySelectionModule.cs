using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_primary_selection_device_manager_v1</c> global through a <see cref="PrimarySelectionManager"/>. It keeps the primary selection in <see cref="ISelectionStore"/>.
/// </summary>
public sealed class PrimarySelectionModule : DesktopModule<PrimarySelectionManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_primary_selection_device_manager_v1";

    /// <inheritdoc/>
    public override int Version => PrimarySelectionManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(ISelectionStore)];

    /// <inheritdoc/>
    protected override PrimarySelectionManager Create(BasinServices services) =>
        new(services.Display, services.Find<ISelectionStore>(), services.Find<Seat.Seat>());
}
