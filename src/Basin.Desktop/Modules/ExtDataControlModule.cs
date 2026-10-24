using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>ext_data_control_manager_v1</c> global through a <see cref="ExtDataControlManager"/>. It reads and writes selections through <see cref="ISelectionStore"/>.
/// </summary>
public sealed class ExtDataControlModule : DesktopModule<ExtDataControlManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "ext_data_control_manager_v1";

    /// <inheritdoc/>
    public override int Version => ExtDataControlManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(ISelectionStore)];

    /// <inheritdoc/>
    protected override ExtDataControlManager Create(BasinServices services) =>
        new(services.Display, services.Find<ISelectionStore>());
}
