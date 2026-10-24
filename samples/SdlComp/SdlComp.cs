using Basin;
using Basin.Backend.Hosted;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Hosted;
using Basin.Ipc;
using Basin.Sdl;
using Basin.Shell.Nested;

namespace SdlComp;

internal sealed partial class SdlComp : IDisposable
{
    private readonly SdlCompOptions _options;
    private readonly BasinLogger _log;
    private readonly SdlLoop _loop;
    private readonly BasinCompositorHost _host;
    private readonly ISdlRenderDevice _device;
    private readonly SdlOutputWindow? _window;
    private readonly NestedShell? _shell;
    private readonly SdlToplevelWindows? _toplevels;
    private readonly SdlFrameLoop _frames;
    private readonly Action<FrameTick>? _tick;
    private readonly SdlCursor _cursor = new();
    private readonly SdlTabletSource _tablet = new();
    private readonly SdlTextInput _textInput = null!;
    private readonly SdlClipboard _clipboard;
    private readonly SdlDrop? _drop;
    private IpcServer? _ipc;
    private bool _disposed;

    public SdlComp(SdlCompOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;
        _loop = new SdlLoop(new SdlLoopOptions { AppName = "sdl-comp", VideoDriver = options.VideoDriver });
        try
        {
            _textInput = new SdlTextInput(_loop);
            _host = new BasinCompositorHost(HostOptions(options.Socket, _tablet, _textInput, PresentsHdr(options.Renderer)) with
            {
                ManagedTransport = options.WaypipeListen is not null || !OperatingSystem.IsLinux(),
            });
        }
        catch
        {
            _loop.Dispose();
            throw;
        }

        _loop.Attach(_host);
        _device = SdlRenderDevices.Create(options.Renderer);
        _clipboard = new SdlClipboard(_host, _loop);
        _textInput.WindowFor = TextInputWindow;
        _textInput.AreaFor = TextInputArea;
        _frames = new SdlFrameLoop(_loop, _host);
        _frames.Continuous = options.Frames > 0;
        if (options.Toplevels)
        {
            _toplevels = new SdlToplevelWindows(_loop, _host, _device, _frames);
            _host.Seat.Pointer.CursorRequested += OnSeatCursor;
            if (_host.Services.Find<Basin.Desktop.CursorShapeManager>() is { } shapes)
            {
                shapes.ShapeRequested += _cursor.Show;
            }

            return;
        }

        _window = new SdlOutputWindow(_loop, _host, _device, new SdlOutputWindowOptions
        {
            Title = Title,
            Width = options.Width,
            Height = options.Height,
            OutputName = NestedShell.OutputKey,
            Scale = options.Scale,
            GrabKeyboard = options.GrabKeyboard,
        });
        var settings = new ShellSettings();
        _shell = new NestedShell(
            _host,
            _window.View,
            settings,
            new PanelLayout(0, 0, 0),
            KeyTable.Build(settings.Keys, []),
            _loop.Post,
            [NestedTheme.Always]);
        _window.InputSink = input => _shell.HandleInput(input);
        _window.ResizeHandler = (width, height, scale) => _shell.Resize(width, height, scale);
        _window.CloseRequested += _ => _loop.RequestQuit();
        _window.Reconfigured += ArrangeOutputs;
        _shell.CursorChanged += _cursor.ShowNamed;
        _shell.ClientCursorChanged += ShowClientCursor;
        _tick = _ => _shell.Tick(Environment.TickCount64);
        _host.Session.BeforeDispatch += _tick;
        _tablet.Locate = LocatePen;
        _loop.AddSink(_tablet);
        _drop = new SdlDrop(_host, _loop, _window);
        _frames.Add(_window);
        for (var i = 1; i < options.Outputs; i++)
        {
            AddOutput(i + 1);
        }
    }

    internal static bool PresentsHdr(string renderer) =>
        renderer is SdlRenderDevices.Vulkan or SdlRenderDevices.Metal;

    internal static BasinCompositorOptions HostOptions(
        string? socket, SdlTabletSource? tablet = null, SdlTextInput? textInput = null, bool colorManagement = false) => new()
    {
        AppName = "sdl-comp",
        SocketName = socket,
        TextInput = textInput,
        ColorManagement = colorManagement,
        ConfigureServices = (_, services) =>
        {
            services
                .Without("zwlr_layer_shell_v1")
                .Without("ext_session_lock_manager_v1");
            if (tablet is not null)
            {
                services.UseDefault<Basin.Capabilities.ITabletSource>(tablet);
            }
        },
    };

    public SdlTabletSource Tablet => _tablet;

    public SdlTextInput TextInput => _textInput;

    private nint TextInputWindow()
    {
        var focused = SDL3.SDL.GetKeyboardFocus();
        foreach (var window in OutputWindows)
        {
            if (window.Window.Handle == focused)
            {
                return focused;
            }
        }

        return _window?.Window.Handle ?? focused;
    }

    private (int X, int Y, int Width, int Height)? TextInputArea(Basin.Surface surface, Basin.Box cursor)
    {
        Basin.Box box;
        if (_shell?.WindowOf(surface) is { } managed)
        {
            box = managed.ClientBox;
        }
        else if (_toplevels?.Entries.FirstOrDefault(e => ReferenceEquals(e.Toplevel.Surface, surface)) is { Window: not null } entry)
        {
            box = new Basin.Box(0, entry.Cell * SdlToplevelWindows.CellStride, 0, 0);
        }
        else
        {
            return null;
        }

        var layoutX = box.X + cursor.X;
        var layoutY = box.Y + cursor.Y;
        foreach (var window in OutputWindows)
        {
            var origin = window.View.SceneOutput.Position;
            var factor = window.View.Target.Scale / window.Window.PixelDensity;
            var (pixelWidth, pixelHeight) = window.Window.PixelSize;
            var x = (layoutX - origin.X) * factor;
            var y = (layoutY - origin.Y) * factor;
            if (x >= 0 && y >= 0 && x * window.Window.PixelDensity < pixelWidth && y * window.Window.PixelDensity < pixelHeight)
            {
                return ((int)x, (int)y, (int)Math.Ceiling(cursor.Width * factor), (int)Math.Ceiling(cursor.Height * factor));
            }
        }

        return null;
    }

    private (double X, double Y)? LocatePen(uint windowId, float x, float y)
    {
        foreach (var window in OutputWindows)
        {
            if (window.Window.Id != windowId)
            {
                continue;
            }

            var (logicalX, logicalY) = window.ToLogical(x, y);
            var origin = window.View.SceneOutput.Position;
            if (_shell is null)
            {
                return null;
            }

            var output = _shell.Output;
            return ((logicalX + origin.X) / output.Width, (logicalY + origin.Y) / output.Height);
        }

        return null;
    }

    private string Title => _host.Socket.Length > 0 ? $"SdlComp — {_host.Socket}" : "SdlComp";

    public long Frames => _frames.Frames;

    public NestedShell Shell => _shell ?? throw new InvalidOperationException("The compositor runs one window per toplevel. This member needs --windows nested.");

    public SdlToplevelWindows? Toplevels => _toplevels;

    public BasinCompositorHost Host => _host;

    public SdlOutputWindow Window => _window ?? throw new InvalidOperationException("The compositor runs one window per toplevel. This member needs --windows nested.");

    public SdlFrameLoop FrameLoop => _frames;

    public SdlCursor Cursor => _cursor;

    public SdlClipboard Clipboard => _clipboard;

    public SdlDrop Drop => _drop ?? throw new InvalidOperationException("The compositor runs one window per toplevel. This member needs --windows nested.");

    private Basin.Surface? _cursorSurface;
    private Action? _cursorCommitted;

    private void OnSeatCursor(Basin.Seat.CursorRequest request)
    {
        if (_cursorSurface is { } previous && _cursorCommitted is { } committed)
        {
            previous.Committed -= committed;
        }

        _cursorSurface = request.Surface;
        _cursorCommitted = null;
        if (request.Surface is not { } surface)
        {
            _cursor.Hide();
            return;
        }

        var (hotspotX, hotspotY) = (request.HotspotX, request.HotspotY);
        _cursorCommitted = () => ShowClientCursor(ShellCursor.Image(surface, hotspotX, hotspotY));
        surface.Committed += _cursorCommitted;
        ShowClientCursor(ShellCursor.Image(surface, hotspotX, hotspotY));
    }

    private void ShowClientCursor(ShellCursor cursor)
    {
        if (cursor.Hidden)
        {
            _cursor.Hide();
        }
        else if (cursor.Shape is { } shape)
        {
            _cursor.Show(shape);
        }
        else if (cursor.Surface is { } surface)
        {
            if (!_cursor.ShowImage(surface, cursor.HotspotX, cursor.HotspotY))
            {
                _cursor.ShowSystem(SDL3.SDL.SystemCursor.Default);
            }
        }
        else
        {
            _cursor.ShowSystem(SDL3.SDL.SystemCursor.Default);
        }
    }

    public SdlLoop Loop => _loop;

    public void Start()
    {
        BasinReport.Line($"SDL driver={_loop.VideoDriver} renderer={_device.Name}");
        BasinReport.Line(CompositorLines.Socket(_host.Socket));
        StartIpc();
        StartListening();
        _window?.Window.Show();
        _loop.MarkActivity();
    }

    public int Run()
    {
        Start();
        while (_frames.RunOnce())
        {
            if (_options.Frames > 0 && _frames.Frames >= _options.Frames)
            {
                break;
            }
        }

        if (_options.Screenshot is { Length: > 0 } path)
        {
            WriteScreenshot(path);
        }

        return 0;
    }

    private void WriteScreenshot(string path)
    {
        var written = false;
        _host.RequestScreenshot(path, ok => written = ok);
        foreach (var window in OutputWindows)
        {
            window.View.RequestRender?.Invoke();
        }

        for (var i = 0; i < 10 && !written; i++)
        {
            _loop.MarkActivity();
            _frames.RunOnce();
        }

        if (written)
        {
            _log.Info($"screenshot written to {path}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ipc?.Dispose();
        _listening?.Dispose();
        if (_tick is not null)
        {
            _host.Session.BeforeDispatch -= _tick;
        }

        foreach (var extra in _extra.ToArray())
        {
            RemoveOutput(extra);
        }

        _drop?.Dispose();
        _clipboard.Dispose();
        _textInput.Dispose();
        _shell?.Dispose();
        _toplevels?.Dispose();
        _cursor.Dispose();
        _frames.Dispose();
        _loop.Detach();
        _window?.Lease.MakeCurrent();
        _host.Dispose();
        _window?.Dispose();
        _device.Dispose();
        _loop.Dispose();
    }
}
