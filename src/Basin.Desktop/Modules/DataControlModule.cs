using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwlr_data_control_manager_v1</c> global through a <see cref="DataControlManager"/>. It reads and writes selections through <see cref="ISelectionStore"/>.
/// </summary>
public sealed class DataControlModule : DesktopModule<DataControlManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwlr_data_control_manager_v1";

    /// <inheritdoc/>
    public override int Version => DataControlManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(ISelectionStore)];

    /// <inheritdoc/>
    protected override DataControlManager Create(BasinServices services) =>
        new(services.Display, services.Find<ISelectionStore>());
}
