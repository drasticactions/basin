using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class ExternalBrightnessModule : PlasmaModule<ExternalBrightnessManager>
{
    public override string WireInterface => "kde_external_brightness_v1";

    public override int Version => ExternalBrightnessManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputSet)];

    protected override ExternalBrightnessManager Create(BasinServices services)
    {
        var manager = new ExternalBrightnessManager(services.Display, services.Loop, services.Find<IOutputSet>());
        services.UseDefault<IOutputBrightness>(manager.Control);
        return manager;
    }
}
