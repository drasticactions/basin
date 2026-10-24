using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwlr_output_power_manager_v1</c> global through a <see cref="OutputPowerManager"/>. It switches outputs through <see cref="IOutputPower"/> when one is registered.
/// </summary>
public sealed class OutputPowerModule : DesktopModule<OutputPowerManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwlr_output_power_manager_v1";

    /// <inheritdoc/>
    public override int Version => OutputPowerManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputPower)];

    /// <inheritdoc/>
    protected override OutputPowerManager Create(BasinServices services) =>
        new(services.Display, services.Find<IOutputPower>());
}
