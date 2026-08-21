using Basin.Capabilities;

namespace Basin.XWayland;

public sealed class XWaylandModule : IProtocolModule
{
    public string WireInterface => "xwayland_shell_v1";

    public int Version => XwaylandShellGlobal.Version;

    public bool IncludeKeyboardGrab { get; init; } = true;

    public IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel)];

    public XwaylandShellGlobal? Shell { get; private set; }

    public XWaylandKeyboardGrabManager? KeyboardGrab { get; private set; }

    public XWaylandServer? Server { get; private set; }

    public XWaylandWm? WindowManager { get; private set; }

    public XWaylandToplevelSource? Toplevels { get; private set; }

    public event Action<XWaylandWm>? WindowManagerReady;

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var compositor = services.Require<CompositorGlobal>();
        var seat = services.Find<Seat.Seat>();
        Shell = new XwaylandShellGlobal(services.Display, compositor);
        Server = new XWaylandServer(services.Display, services.Loop);
        services.Use(Shell);
        services.Use(Server);
        if (IncludeKeyboardGrab)
        {
            KeyboardGrab = new XWaylandKeyboardGrabManager(services.Display, compositor, seat);
            services.Use(KeyboardGrab);
            var server = Server;
            KeyboardGrab.RestrictTo(client => ReferenceEquals(client, server.Client));
        }

        var model = services.Find<IToplevelModel>() as AggregateToplevelModel;
        Server.Ready += wmFd =>
        {
            WindowManager = new XWaylandWm(wmFd, services.Loop, Shell, seat);
            Toplevels = new XWaylandToplevelSource(WindowManager);
            model?.Add(Toplevels);
            WindowManagerReady?.Invoke(WindowManager);
        };
        Server.Exited += () =>
        {
            Toplevels?.Dispose();
            WindowManager?.Dispose();
            WindowManager = null;
            Toplevels = null;
        };

        return new Handle(this);
    }

    private sealed class Handle : IDisposable
    {
        private readonly XWaylandModule _module;

        public Handle(XWaylandModule module) => _module = module;

        public void Dispose()
        {
            _module.Toplevels?.Dispose();
            _module.WindowManager?.Dispose();
            _module.Server?.Dispose();
            _module.KeyboardGrab?.Dispose();
            _module.Shell?.Dispose();
        }
    }
}
