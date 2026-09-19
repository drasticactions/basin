using Basin.Backend.Hosted;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Shell.Xdg;
using Wayland.Server;
using static Basin.Hosted.HostedLog;

namespace Basin.Hosted;

public sealed class BasinCompositorHost : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    public ThreadAffinity Affinity => _thread;
    private bool _disposed;

    public BasinCompositorHost(BasinCompositorOptions? options = null)
    {
        options ??= new BasinCompositorOptions();
        Renderer = new HostedRenderer();
        Display = options.ManagedTransport
            ? WlServerDisplay.Create(new ManagedTransport(new ManagedTransportOptions { LocalSocket = PlatformFacts.HasLocalClients }))
            : WlServerDisplay.Create();
        var localClients = Display.SupportsLocalSocket && PlatformFacts.HasLocalClients;
        if (localClients &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")))
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", CreateRuntimeDirectory());
        }

        Socket = localClients
            ? options.SocketName is { } name ? AddNamedSocket(name) : Display.AddSocketAuto()
            : string.Empty;
        Loop = new WaylandEventLoop(Display);
        Backend = new HostedBackend();
        Scene = new Scene.Scene();
        Layout = new OutputLayout();
        var frames = new Capabilities.Defaults.FrameClock();
        var services = new BasinServices(Display, Loop)
            .Use(Layout)
            .Use<Capabilities.IFrameClock>(frames)
            .Use<Capabilities.IActivationTokens>(new Capabilities.Defaults.DefaultActivationTokens())
            .Use<Capabilities.IBell>(Capabilities.Defaults.SilentBell.Instance);
        if (options.TextInput is { } textInput)
        {
            services = services.Use<Capabilities.ITextInputMethod>(textInput);
        }

        services = services.Install(DesktopPack.For(options.AppName)).Without("wp_color_manager_v1");
        if (options.ExtraModules is { } extras)
        {
            foreach (var module in extras)
            {
                services = services.Install(module);
            }
        }

        Services = services.Freeze();
        Shell = Services.Require<XdgShell>();
        Seat = Services.Require<Seat.Seat>();
        if (PlatformFacts.HasHostKeyboardLayout)
        {
            var hostKeymaps = new global::Basin.Seat.HostKeymapSource();
            Seat.Keyboard.KeymapSource = hostKeymaps;
            Seat.Keyboard.SetKeymapFromHost();
            hostKeymaps.Changed += () => _hostKeymapDirty = true;
        }
        else if (PlatformFacts.HasSystemXkbData)
        {
            Seat.Keyboard.SetKeymap(global::Basin.Seat.SystemKeymap.Read());
        }
        else
        {
            Seat.Keyboard.SetKeymapFromBuffer(
                System.Text.Encoding.UTF8.GetBytes(global::Basin.Seat.HostKeyboardLayout.FallbackKeymapText));
        }

        Keys = new global::Basin.Seat.KeySynthesizer(Seat.Keyboard);
        if (Services.Find<TextInputManager>() is { } textInputManager)
        {
            Seat.Keyboard.FocusChanged += surface => textInputManager.NotifyFocus(surface);
            textInputManager.CommitStringUnclaimed += TypeUnclaimed;
        }

        Session = new HostedSession(Display, Loop) { Frames = frames };
        Screens = new HostScreens(this);
        Wake = new HostedWakeSource(Loop);
        Renderer.EglAvailable += OnEglAvailable;
    }

    private LinuxDmabufGlobal? _dmabuf;

    public LinuxDmabufGlobal? Dmabuf => _dmabuf;

    private void OnEglAvailable(HostedEglImport import)
    {
        if (_disposed || _dmabuf is not null || !HostedEglImport.IsSupported)
        {
            return;
        }

        var node = import.RenderNodePath ?? "/dev/dri/renderD128";
        if (!File.Exists(node))
        {
            Log.Info($"no render node for dmabuf feedback; the global stays withheld");
            return;
        }

        _dmabuf = new LinuxDmabufGlobal(
            Display,
            Services.Require<ClientBufferRegistry>(),
            import.Formats,
            node,
            compositor: Services.Require<CompositorGlobal>());
        Log.Info($"dmabuf advertised with {import.Formats.Count} format rows on {node}");
    }

    public HostScreens Screens { get; }

    public WlServerDisplay Display { get; }

    public string Socket { get; }

    public WaylandEventLoop Loop { get; }

    public HostedBackend Backend { get; }

    public Scene.Scene Scene { get; }

    public OutputLayout Layout { get; }

    public BasinServices Services { get; }

    public XdgShell Shell { get; }

    public Seat.Seat Seat { get; }

    public HostedSession Session { get; }

    public HostedWakeSource Wake { get; }

    public HostedRenderer Renderer { get; }

    public global::Basin.Seat.KeySynthesizer Keys { get; }

    private void TypeUnclaimed(string text)
    {
        _thread.Assert();
        Keys.Keymap = Seat.Keyboard.Keymap;
        Keys.Layout = Seat.Keyboard.State?.SerializeLayout(Xkb.XkbStateComponent.LayoutEffective) ?? 0;
        Keys.Type(text, (uint)Environment.TickCount);
    }

    public void CancelTouch()
    {
        _thread.Assert();
        Seat.Touch.NotifyCancel();
    }

    private bool _suspended;

    public bool IsSuspended => _suspended;

    public void Suspend()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        Wake.Suspend();
        Session.Suspend();
        foreach (var view in _views)
        {
            view.SetPresenting(false);
        }
    }

    public void Resume()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        Session.Resume();
        Wake.Resume();
        foreach (var view in _views)
        {
            view.SetPresenting(true);
            view.RequestRender?.Invoke();
        }
    }

    private readonly List<BasinViewOutput> _views = [];
    private TimeSpan _frameStamp = TimeSpan.MinValue;
    private bool _frameOpen;

    public event Action<long>? Composited;

    private string? _screenshotPath;
    private Action<bool>? _screenshotDone;

    public void RequestScreenshot(string path, Action<bool> done)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(done);
        _thread.Assert();
        _screenshotPath = path;
        _screenshotDone = done;
    }

    internal bool TakeScreenshotRequest(out string path, out Action<bool> done)
    {
        path = _screenshotPath!;
        done = _screenshotDone!;
        if (_screenshotPath is null || _screenshotDone is null)
        {
            return false;
        }

        _screenshotPath = null;
        _screenshotDone = null;
        return true;
    }

    internal void NotifyComposited() => Composited?.Invoke(Session.Composited);

    public void InvalidateDirtyViews()
    {
        _thread.Assert();
        if (_suspended)
        {
            return;
        }

        foreach (var view in _views)
        {
            if (view.SceneOutput.NeedsRepaint)
            {
                view.RequestRender?.Invoke();
            }
        }
    }

    private volatile bool _hostKeymapDirty;

    public bool EnterFrame(TimeSpan stamp)
    {
        _thread.Assert();
        if (_suspended || _frameOpen || stamp == _frameStamp)
        {
            return false;
        }

        _frameStamp = stamp;
        _frameOpen = true;
        if (_hostKeymapDirty)
        {
            _hostKeymapDirty = false;
            Seat.Keyboard.SetKeymapFromHost();
        }

        Session.BeginFrame();
        return true;
    }

    public void ExitFrame()
    {
        _thread.Assert();
        if (!_frameOpen)
        {
            throw new InvalidOperationException("ExitFrame without a matching EnterFrame.");
        }

        _frameOpen = false;
        Session.EndFrame();
    }

    public BasinViewOutput CreateViewOutput(int width, int height, double scale = 1.0, string? name = null)
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var output = Backend.CreateOutput(new OutputMode(width, height, 60_000), scale, name);
        var sceneOutput = new Scene.SceneOutput(Scene, output);
        var view = new BasinViewOutput(this, output, sceneOutput);
        view.SetPresenting(!_suspended);
        Session.AddOutput(sceneOutput);
        _views.Add(view);
        return view;
    }

    internal void ForgetView(BasinViewOutput view) => _views.Remove(view);

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Wake.Dispose();
        foreach (var view in _views.ToArray())
        {
            view.Dispose();
        }

        Session.Dispose();
        Screens.Dispose();
        _dmabuf?.Dispose();
        Services.Dispose();
        Scene.Root.Destroy();
        Backend.Dispose();
        Display.Dispose();
        Renderer.Dispose();
    }

    private string AddNamedSocket(string name)
    {
        Display.AddSocket(name);
        return name;
    }

    private static string CreateRuntimeDirectory()
    {
        if (!PlatformFacts.HasLocalClients)
        {
            throw new PlatformNotSupportedException("a runtime directory serves local clients, which this host has none of");
        }

        var directory = Path.Combine(Path.GetTempPath(), $"basin-{Environment.UserName}");
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Log.Debug($"XDG_RUNTIME_DIR is unset; sockets bind under {directory}");
        return directory;
    }
}
