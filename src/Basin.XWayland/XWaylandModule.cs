using Basin.Capabilities;

namespace Basin.XWayland;

/// <summary>
/// Installs <c>xwayland_shell_v1</c> and the native Xwayland server, with an optional low-density server and the Xwayland keyboard grab. Each server starts when its first X client connects.
/// </summary>
public sealed class XWaylandModule : IProtocolModule
{
    private readonly List<XWaylandDisplay> _displays = [];

    /// <inheritdoc/>
    public string WireInterface => "xwayland_shell_v1";

    /// <inheritdoc/>
    public int Version => XwaylandShellGlobal.Version;

    /// <summary>
    /// Gets a value indicating whether the install also creates <see cref="XWaylandKeyboardGrabManager"/>, limited to the Xwayland clients.
    /// </summary>
    public bool IncludeKeyboardGrab { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the install also creates a second server at a fixed scale of 1.
    /// </summary>
    public bool IncludeLowDensity { get; init; }

    /// <summary>
    /// Gets a value indicating whether each toplevel source joins the registered <see cref="IToplevelModel"/>. It must be an <see cref="AggregateToplevelModel"/> to join.
    /// </summary>
    public bool PublishToplevels { get; init; } = true;

    /// <summary>
    /// Gets the capabilities that the module consumes. The module reads <see cref="IToplevelModel"/> to publish the X11 windows.
    /// </summary>
    public IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel)];

    /// <summary>
    /// Gets the shell global. It is <see langword="null"/> before <see cref="Install"/>.
    /// </summary>
    public XwaylandShellGlobal? Shell { get; private set; }

    /// <summary>
    /// Gets the keyboard grab global, or <see langword="null"/> before the install or when <see cref="IncludeKeyboardGrab"/> is <see langword="false"/>.
    /// </summary>
    public XWaylandKeyboardGrabManager? KeyboardGrab { get; private set; }

    /// <summary>
    /// Gets every display that the install created, the native display first.
    /// </summary>
    public IReadOnlyList<XWaylandDisplay> Displays => _displays;

    /// <summary>
    /// Gets the display that runs at the output scale. It is <see langword="null"/> only before the install.
    /// </summary>
    public XWaylandDisplay? Native { get; private set; }

    /// <summary>
    /// Gets the display at scale 1, or <see langword="null"/> when <see cref="IncludeLowDensity"/> is <see langword="false"/>.
    /// </summary>
    public XWaylandDisplay? LowDensity { get; private set; }

    /// <summary>
    /// Gets a value indicating whether each window manager takes the XSETTINGS selection.
    /// </summary>
    public bool PublishXSettings { get; init; } = true;

    /// <summary>
    /// Creates the shell global, the displays and the keyboard grab, and registers the shell and the grab in <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The registry. It must hold a <see cref="CompositorGlobal"/>.</param>
    /// <returns>A handle that disposes the displays, the keyboard grab and the shell.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var compositor = services.Require<CompositorGlobal>();
        var seat = services.Find<Seat.Seat>();
        Shell = new XwaylandShellGlobal(services.Display, compositor);
        services.Use(Shell);
        var model = PublishToplevels ? services.Find<IToplevelModel>() as AggregateToplevelModel : null;
        var layout = services.Find<OutputLayout>();
        var clientScales = services.Find<ClientScales>() ?? compositor.ClientScales;
        var placement = services.Find<IToplevelPlacement>();

        XWaylandDisplay Create(X11Density density, XWaylandScale scale)
        {
            var server = new XWaylandServer(services.Display, services.Loop) { X11Scale = scale };
            var outputs = layout is null
                ? null
                : new XWaylandOutputs(server, layout, clientScales, services.Find<ILogicalOutputOverrides>);
            var display = new XWaylandDisplay(density, server, outputs, services.Loop, Shell, seat, model, PublishXSettings) { Placement = placement };
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

    /// <summary>
    /// Tells whether a Wayland client is the running Xwayland process of one of the displays.
    /// </summary>
    /// <param name="client">The client to check.</param>
    /// <returns><see langword="true"/> when a display currently runs <paramref name="client"/>.</returns>
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

    /// <summary>
    /// Chooses the display for a launch. A low-density request gets the low-density display only when it exists and the native scale is greater than 1.
    /// </summary>
    /// <param name="density">The requested density.</param>
    /// <returns>The display, or <see langword="null"/> before the install.</returns>
    public XWaylandDisplay? DisplayFor(X11Density density)
    {
        if (density == X11Density.Low && LowDensity is { } low && Native is { } native && native.Scale > 1)
        {
            return low;
        }

        return Native;
    }

    /// <summary>
    /// Finds the display whose current window manager is <paramref name="wm"/>.
    /// </summary>
    /// <param name="wm">The window manager.</param>
    /// <returns>The display, or <see langword="null"/> when the window manager is gone or belongs to no display of this module.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Finds the display of an X11 window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The display, or <see langword="null"/> when the window manager of the window is gone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public XWaylandDisplay? DisplayOf(XWaylandWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return DisplayOf(window.WindowManager);
    }

    /// <summary>
    /// Sets the scale and the DPI buckets of the native server. The low-density server keeps scale 1 and no buckets.
    /// </summary>
    /// <param name="scale">The X11 scale.</param>
    /// <param name="dpiBuckets">The allowed DPI values. An empty list publishes the exact DPI.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dpiBuckets"/> is <see langword="null"/>.</exception>
    public void Configure(XWaylandScale scale, IReadOnlyList<int> dpiBuckets)
    {
        ArgumentNullException.ThrowIfNull(dpiBuckets);
        if (Native is { } native)
        {
            native.Server.X11Scale = scale;
            native.Server.DpiBuckets = dpiBuckets;
        }
    }

    /// <summary>
    /// Builds the variables for a child process. They hold the cursor variables of the native display, and <see cref="X11Route.LowDensityDisplayVariable"/> when a low-density display exists.
    /// </summary>
    /// <returns>A new map of variable names to values.</returns>
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

    /// <summary>
    /// Gives the display name and the environment for a launch at a density. It is shaped to be the route function of a process tracker.
    /// </summary>
    /// <param name="density">The requested density.</param>
    /// <returns>The route of <see cref="DisplayFor"/>, or <see langword="null"/> before the install.</returns>
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
