using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class PlasmaVirtualDesktopModule : DesktopModule<PlasmaVirtualDesktopManager>
{
    public override string WireInterface => "org_kde_plasma_virtual_desktop_management";

    public override int Version => PlasmaVirtualDesktopManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IWorkspaceModel)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.UseDefault<IWorkspaceModel>(EmptyWorkspaceModel.Instance);
    }

    protected override PlasmaVirtualDesktopManager Create(BasinServices services) =>
        new(services.Display, services.Find<IWorkspaceModel>());
}
