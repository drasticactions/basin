namespace Basin.Plasma;

public sealed class ScreenEdgeModule : IProtocolModule
{
    private PlasmaScreenEdges? _seededEdges;

    public string WireInterface => "kde_screen_edge_manager_v1";

    public int Version => ScreenEdgeManager.Version;

    public ScreenEdgeManager? Manager { get; private set; }

    public void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<PlasmaScreenEdges>() is null &&
            services.Find<OutputLayout>() is { } layout)
        {
            _seededEdges = new PlasmaScreenEdges(
                services.Loop, services.Find<Basin.Seat.Seat>(), layout);
            services.UseDefault(_seededEdges);
        }
    }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<PlasmaScreenEdges>() is { } edges)
        {
            edges.Seat ??= services.Find<Basin.Seat.Seat>();
        }

        Manager = new ScreenEdgeManager(
            services.Display,
            services.Require<CompositorGlobal>(),
            services.Find<Scene.Scene>(),
            services.Find<OutputLayout>(),
            services.Find<PlasmaScreenEdges>());
        services.Use(Manager);
        return new Handle(this);
    }

    private sealed class Handle : IDisposable
    {
        private readonly ScreenEdgeModule _module;

        public Handle(ScreenEdgeModule module) => _module = module;

        public void Dispose()
        {
            _module.Manager?.Dispose();
            _module._seededEdges?.Dispose();
        }
    }
}
