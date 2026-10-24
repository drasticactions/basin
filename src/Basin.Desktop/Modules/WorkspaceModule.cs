using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>ext_workspace_manager_v1</c> global through a <see cref="WorkspaceManager"/>. It seeds an empty <see cref="IWorkspaceModel"/> unless the consumer registers one.
/// </summary>
public sealed class WorkspaceModule : DesktopModule<WorkspaceManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "ext_workspace_manager_v1";

    /// <inheritdoc/>
    public override int Version => WorkspaceManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IWorkspaceModel)];

    /// <inheritdoc/>
    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.UseDefault<IWorkspaceModel>(EmptyWorkspaceModel.Instance);
    }

    /// <inheritdoc/>
    protected override WorkspaceManager Create(BasinServices services) =>
        new(services.Display, services.Find<IWorkspaceModel>());
}
