using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class GammaControlModule : DesktopModule<GammaControlManager>
{
    public override string WireInterface => "zwlr_gamma_control_manager_v1";

    public override int Version => GammaControlManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputGamma)];

    protected override GammaControlManager Create(BasinServices services) =>
        new(services.Display, services.Find<IOutputGamma>());
}
