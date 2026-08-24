using Basin.Capabilities;
using Basin.Shell.Xdg;

namespace Basin.Plasma;

public sealed class PlasmaShellModule : IProtocolModule
{
    private PlasmaShellPlacement? _seededPlacement;
    private PlasmaScreenEdges? _seededEdges;

    public string WireInterface => "org_kde_plasma_shell";

    public int Version => PlasmaShellManager.Version;

    public PlasmaShellManager? Manager { get; private set; }

    public void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<PlasmaShellPlacement>() is null &&
            services.Find<Scene.Scene>() is { } scene &&
            services.Find<OutputLayout>() is { } layout)
        {
            _seededPlacement = new PlasmaShellPlacement(scene, layout, services.Find<IOutputSet>());
            services.UseDefault(_seededPlacement);
        }

        if (services.Find<PlasmaScreenEdges>() is null &&
            services.Find<OutputLayout>() is { } edgeLayout)
        {
            _seededEdges = new PlasmaScreenEdges(
                services.Loop, services.Find<Basin.Seat.Seat>(), edgeLayout);
            services.UseDefault(_seededEdges);
        }
    }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new PlasmaShellManager(
            services.Display,
            services.Require<CompositorGlobal>(),
            services.Find<XdgToplevelSource>());
        services.Use(Manager);
        if (services.Find<PlasmaScreenEdges>() is { } edges)
        {
            edges.Seat ??= services.Find<Basin.Seat.Seat>();
        }

        if (services.Find<PlasmaShellPlacement>() is { } placement)
        {
            placement.Seat ??= services.Find<Basin.Seat.Seat>();
            placement.ScreenEdges ??= services.Find<PlasmaScreenEdges>();
            placement.Attach(Manager);
        }

        return new Handle(this);
    }

    private sealed class Handle : IDisposable
    {
        private readonly PlasmaShellModule _module;

        public Handle(PlasmaShellModule module) => _module = module;

        public void Dispose()
        {
            _module.Manager?.Dispose();
            _module._seededEdges?.Dispose();
            _module._seededPlacement?.Dispose();
        }
    }
}
