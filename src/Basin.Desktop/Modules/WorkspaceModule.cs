using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class WorkspaceModule : DesktopModule<WorkspaceManager>
{
    public override string WireInterface => "ext_workspace_manager_v1";

    public override int Version => WorkspaceManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IWorkspaceModel)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.UseDefault<IWorkspaceModel>(EmptyWorkspaceModel.Instance);
    }

    protected override WorkspaceManager Create(BasinServices services) =>
        new(services.Display, services.Find<IWorkspaceModel>());
}
