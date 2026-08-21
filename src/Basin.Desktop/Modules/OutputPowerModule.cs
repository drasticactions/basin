using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class OutputPowerModule : DesktopModule<OutputPowerManager>
{
    public override string WireInterface => "zwlr_output_power_manager_v1";

    public override int Version => OutputPowerManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputPower)];

    protected override OutputPowerManager Create(BasinServices services) =>
        new(services.Display, services.Find<IOutputPower>());
}
