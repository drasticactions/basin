using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class PlasmaWindowManagementModule : DesktopModule<PlasmaWindowManager>
{
    public override string WireInterface => "org_kde_plasma_window_management";

    public override int Version => PlasmaWindowManager.Version;

    public override IReadOnlyList<Type> Capabilities =>
        [typeof(IToplevelModel), typeof(IWorkspaceModel), typeof(IToplevelStack)];

    protected override PlasmaWindowManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Find<IToplevelModel>(),
            services.Find<IWorkspaceModel>(),
            services.Find<IToplevelStack>(),
            services.Find<CompositorGlobal>());
}
