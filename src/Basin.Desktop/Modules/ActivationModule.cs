using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ActivationModule : DesktopModule<XdgActivationManager>
{
    public override string WireInterface => "xdg_activation_v1";

    public override int Version => XdgActivationManager.Version;

    public override IReadOnlyList<Type> Drivers => [typeof(IActivationTokens)];

    protected override XdgActivationManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<IActivationTokens>());
}
