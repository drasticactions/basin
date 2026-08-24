using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class PlasmaOutputManagementModule : PlasmaModule<PlasmaOutputManagementManager>
{
    public override string WireInterface => "kde_output_management_v2";

    public override int Version => PlasmaOutputManagementManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputConfiguration)];

    protected override PlasmaOutputManagementManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Find<PlasmaOutputDeviceManager>(),
            services.Find<IOutputConfiguration>());
}
