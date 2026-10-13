using Basin.Capabilities;
using System.Globalization;

namespace Basin.XWayland;

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

    public X11Density Density { get; }

    public XWaylandServer Server { get; }

    public XWaylandOutputs? Outputs { get; }

    public XWaylandWm? WindowManager { get; private set; }

    public XWaylandToplevelSource? Toplevels { get; private set; }

    public string DisplayName => Server.DisplayName;

    public double Scale => Outputs?.Scale ?? (Server.X11Scale.IsAuto ? 1.0 : Server.X11Scale.Value);

    public event Action<XWaylandWm>? WindowManagerReady;

    public event Action? Exited;

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
        Toplevels = new XWaylandToplevelSource(WindowManager, this);
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
