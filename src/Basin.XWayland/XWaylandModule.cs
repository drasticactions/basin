using Basin.Capabilities;

namespace Basin.XWayland;

public sealed class XWaylandModule : IProtocolModule
{
    private readonly List<XWaylandDisplay> _displays = [];

    public string WireInterface => "xwayland_shell_v1";

    public int Version => XwaylandShellGlobal.Version;

    public bool IncludeKeyboardGrab { get; init; } = true;

    public bool IncludeLowDensity { get; init; }

    public IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel)];

    public XwaylandShellGlobal? Shell { get; private set; }

    public XWaylandKeyboardGrabManager? KeyboardGrab { get; private set; }

    public IReadOnlyList<XWaylandDisplay> Displays => _displays;

    public XWaylandDisplay? Native { get; private set; }

    public XWaylandDisplay? LowDensity { get; private set; }

    public bool PublishXSettings { get; init; } = true;

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var compositor = services.Require<CompositorGlobal>();
        var seat = services.Find<Seat.Seat>();
        Shell = new XwaylandShellGlobal(services.Display, compositor);
        services.Use(Shell);
        var model = services.Find<IToplevelModel>() as AggregateToplevelModel;
        var layout = services.Find<OutputLayout>();
        var clientScales = services.Find<ClientScales>() ?? compositor.ClientScales;

        XWaylandDisplay Create(X11Density density, XWaylandScale scale)
        {
            var server = new XWaylandServer(services.Display, services.Loop) { X11Scale = scale };
            var outputs = layout is null
                ? null
                : new XWaylandOutputs(server, layout, clientScales, services.Find<ILogicalOutputOverrides>);
            var display = new XWaylandDisplay(density, server, outputs, services.Loop, Shell, seat, model, PublishXSettings);
            _displays.Add(display);
            if (services.Find<Capabilities.Defaults.PopupDirectory>() is { } popups)
            {
                XWaylandPopupDirectory? current = null;
                display.WindowManagerReady += wm =>
                {
                    if (current is not null)
                    {
                        popups.Remove(current);
                    }

                    current = new XWaylandPopupDirectory(wm);
                    popups.Add(current);
                };
            }

            return display;
        }

        Native = Create(X11Density.Native, XWaylandScale.Auto);
        if (IncludeLowDensity)
        {
            LowDensity = Create(X11Density.Low, XWaylandScale.Fixed(1));
            var native = Native.Server;
            var low = LowDensity.Server;
            native.SettingsChanged += () =>
            {
                low.CursorTheme = native.CursorTheme;
                low.CursorSize = native.CursorSize;
            };
        }

        if (IncludeKeyboardGrab)
        {
            KeyboardGrab = new XWaylandKeyboardGrabManager(services.Display, compositor, seat);
            services.Use(KeyboardGrab);
            KeyboardGrab.RestrictTo(IsXwayland);
        }

        return new Handle(this);
    }

    public bool IsXwayland(Wayland.Server.WlClient client)
    {
        foreach (var display in _displays)
        {
            if (ReferenceEquals(client, display.Server.Client))
            {
                return true;
            }
        }

        return false;
    }

    public XWaylandDisplay? DisplayFor(X11Density density)
    {
        if (density == X11Density.Low && LowDensity is { } low && Native is { } native && native.Scale > 1)
        {
            return low;
        }

        return Native;
    }

    public XWaylandDisplay? DisplayOf(XWaylandWm wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        foreach (var display in _displays)
        {
            if (ReferenceEquals(display.WindowManager, wm))
            {
                return display;
            }
        }

        return null;
    }

    public XWaylandDisplay? DisplayOf(XWaylandWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return DisplayOf(window.WindowManager);
    }

    public void Configure(XWaylandScale scale, IReadOnlyList<int> dpiBuckets)
    {
        ArgumentNullException.ThrowIfNull(dpiBuckets);
        if (Native is { } native)
        {
            native.Server.X11Scale = scale;
            native.Server.DpiBuckets = dpiBuckets;
        }
    }

    public IReadOnlyDictionary<string, string> ChildEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Native is { } native)
        {
            foreach (var (name, value) in native.ChildEnvironment())
            {
                values[name] = value;
            }
        }

        if (LowDensity is not null && DisplayFor(X11Density.Low) is { } low)
        {
            values[X11Route.LowDensityDisplayVariable] = low.DisplayName;
        }

        return values;
    }

    public X11Route? Route(X11Density density) =>
        DisplayFor(density) is { } display ? new X11Route(display.DisplayName, display.ChildEnvironment()) : null;

    private sealed class Handle : IDisposable
    {
        private readonly XWaylandModule _module;

        public Handle(XWaylandModule module) => _module = module;

        public void Dispose()
        {
            foreach (var display in _module._displays)
            {
                display.Dispose();
            }

            _module.KeyboardGrab?.Dispose();
            _module.Shell?.Dispose();
        }
    }
}
