using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class OutputManagementModule : DesktopModule<OutputManagementManager>
{
    public override string WireInterface => "zwlr_output_manager_v1";

    public override int Version => OutputManagementManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputConfiguration), typeof(IOutputSet)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<OutputLayout>() is { } layout)
        {
            services.UseDefault<IOutputConfiguration>(new LayoutOutputConfiguration(layout));
            services.UseDefault<IOutputSet>(new LayoutOutputSet(layout));
        }
    }

    protected override OutputManagementManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<OutputLayout>(),
            services.Require<IOutputSet>(),
            services.Find<IOutputConfiguration>());
}
