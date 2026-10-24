using Basin.Capabilities;
using System.Globalization;

namespace Basin.XWayland;

/// <summary>
/// Joins one Xwayland server to its window manager, its output scale and its toplevel source. It creates a new <see cref="XWaylandWm"/> each time the server starts.
/// </summary>
public sealed class XWaylandDisplay : IDisposable
{
    private readonly ICompositorEventLoop _loop;
    private readonly XwaylandShellGlobal _shell;
    private readonly Seat.Seat? _seat;
    private readonly AggregateToplevelModel? _model;
    private readonly bool _publishXSettings;
    private bool _disposed;

    internal XWaylandDisplay(
        X11Density density,
        XWaylandServer server,
        XWaylandOutputs? outputs,
        ICompositorEventLoop loop,
        XwaylandShellGlobal shell,
        Seat.Seat? seat,
        AggregateToplevelModel? model,
        bool publishXSettings)
    {
        Density = density;
        Server = server;
        Outputs = outputs;
        _loop = loop;
        _shell = shell;
        _seat = seat;
        _model = model;
        _publishXSettings = publishXSettings;
        if (outputs is not null)
        {
            outputs.ScaleChanged += scale => WindowManager?.SetScale(scale);
        }

        server.SettingsChanged += () => WindowManager?.UpdateOptions(WmOptions());
        server.Ready += OnReady;
        server.Exited += OnExited;
    }

    /// <summary>
    /// Gets the density class of this server, native or low density.
    /// </summary>
    public X11Density Density { get; }

    /// <summary>
    /// Gets the server process that this display owns and disposes.
    /// </summary>
    public XWaylandServer Server { get; }

    /// <summary>
    /// Gets the output scale tracker, or <see langword="null"/> when the compositor has no output layout.
    /// </summary>
    public XWaylandOutputs? Outputs { get; }

    /// <summary>
    /// Gets the window manager of the running server. It is <see langword="null"/> before the server is ready and after it exits.
    /// </summary>
    public XWaylandWm? WindowManager { get; private set; }

    /// <summary>
    /// Gets the toplevel source of the running server. It has the same lifetime as <see cref="WindowManager"/>.
    /// </summary>
    public XWaylandToplevelSource? Toplevels { get; private set; }

    /// <summary>
    /// Gets or sets the policy that each new <see cref="Toplevels"/> source gives move and resize requests to.
    /// </summary>
    public IToplevelPlacement? Placement { get; set; }

    /// <summary>
    /// Gets the X display name, for example <c>:1</c>.
    /// </summary>
    public string DisplayName => Server.DisplayName;

    /// <summary>
    /// Gets the scale that X11 clients render at. Without <see cref="Outputs"/>, it is the fixed server scale, or 1 when the scale is automatic.
    /// </summary>
    public double Scale => Outputs?.Scale ?? (Server.X11Scale.IsAuto ? 1.0 : Server.X11Scale.Value);

    /// <summary>
    /// Occurs after each server start, when the new window manager and toplevel source exist.
    /// </summary>
    public event Action<XWaylandWm>? WindowManagerReady;

    /// <summary>
    /// Occurs after the server process exits and the window manager is gone. The next X client starts the server again.
    /// </summary>
    public event Action? Exited;

    /// <summary>
    /// Builds the cursor variables that an X11 child process needs to match the compositor cursor.
    /// </summary>
    /// <returns>A new map with <c>XCURSOR_SIZE</c> at the X11 scale, and <c>XCURSOR_THEME</c> when a theme is set.</returns>
    public IReadOnlyDictionary<string, string> ChildEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["XCURSOR_SIZE"] = ((int)Math.Round(Server.CursorSize * Scale)).ToString(CultureInfo.InvariantCulture),
        };
        if (Server.CursorTheme.Length > 0)
        {
            values["XCURSOR_THEME"] = Server.CursorTheme;
        }

        return values;
    }

    /// <summary>
    /// Disposes the window manager, the toplevel source, the output tracker and the server. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DropWindowManager();
        Outputs?.Dispose();
        Server.Dispose();
    }

    private XWaylandWmOptions WmOptions() => new()
    {
        Scale = Scale,
        DpiBuckets = Server.DpiBuckets,
        CursorTheme = Server.CursorTheme,
        CursorSize = Server.CursorSize,
        PublishXSettings = _publishXSettings,
    };

    private void OnReady(int wmFd)
    {
        WindowManager = new XWaylandWm(wmFd, _loop, _shell, _seat, WmOptions(), Server.Client);
        Toplevels = new XWaylandToplevelSource(WindowManager, this) { Placement = Placement };
        _model?.Add(Toplevels);
        WindowManagerReady?.Invoke(WindowManager);
    }

    private void OnExited()
    {
        DropWindowManager();
        Exited?.Invoke();
    }

    private void DropWindowManager()
    {
        Toplevels?.Dispose();
        WindowManager?.Dispose();
        WindowManager = null;
        Toplevels = null;
    }
}
