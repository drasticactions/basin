using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Plasma;

public sealed class OutputOrderModule : PlasmaModule<OutputOrderManager>
{
    public override string WireInterface => "kde_output_order_v1";

    public override int Version => OutputOrderManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputOrder)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<OutputLayout>() is { } layout)
        {
            services.UseDefault<IOutputSet>(new LayoutOutputSet(layout));
        }

        services.UseDefault<IOutputOrder>(
            new LayoutOutputOrder(services.Find<IOutputSet>(), services.Find<OutputLayout>()));
    }

    protected override OutputOrderManager Create(BasinServices services) =>
        new(services.Display, services.Require<IOutputOrder>());
}
