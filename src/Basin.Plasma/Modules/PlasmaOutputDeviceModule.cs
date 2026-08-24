using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Plasma;

public sealed class PlasmaOutputDeviceModule : PlasmaModule<PlasmaOutputDeviceManager>
{
    public override string WireInterface => "kde_output_device_registry_v2";

    public override int Version => PlasmaOutputDeviceManager.Version;

    public override IReadOnlyList<Type> Capabilities =>
        [typeof(IOutputSet), typeof(IOutputConfiguration), typeof(IOutputOrder)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<OutputLayout>() is { } layout)
        {
            services.UseDefault<IOutputConfiguration>(new LayoutOutputConfiguration(layout));
            services.UseDefault<IOutputSet>(new LayoutOutputSet(layout));
        }
    }

    protected override PlasmaOutputDeviceManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<OutputLayout>(),
            services.Find<IOutputSet>(),
            services.Find<IOutputConfiguration>(),
            services.Find<IOutputOrder>());
}
