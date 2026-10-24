using Basin.Backend.Hosted;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Shell.Xdg;
using Wayland.Server;
using static Basin.Hosted.HostedLog;

namespace Basin.Hosted;

/// <summary>
/// The object graph of a compositor that a toolkit or a window hosts: display, loop, backend, scene, services, seat and renderer.
/// It captures the thread that constructs it, and only that thread can use it.
/// </summary>
public sealed class BasinCompositorHost : IDisposable, Scene.IPresentedOutputs
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    /// <summary>
    /// Gets the thread affinity that the constructor captured. A host view can adopt it for the length of a callback on another thread.
    /// </summary>
    public ThreadAffinity Affinity => _thread;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinCompositorHost"/> class.
    /// It binds the socket, installs the desktop pack and freezes the services. On failure it disposes what it built and rethrows.
    /// </summary>
    /// <param name="options">The construction choices, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="InvalidOperationException">A protocol module has no driver that it needs and the options do not remove it.</exception>
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

        if (options.ColorManagement)
        {
            Color = new ColorCapabilityPack(Layout, Renderer);
            services = services.With(Color).Install(DesktopPack.For(options.AppName));
        }
        else
        {
            services = services.Install(DesktopPack.For(options.AppName)).Without("wp_color_manager_v1");
        }
        if (options.ExtraModules is { } extras)
        {
            foreach (var module in extras)
            {
                services = services.Install(module);
            }
        }

        services = services.SeedDefault<Capabilities.ISceneDiagnostics>(
            () => new Scene.SceneDiagnostics(Scene, Renderer, Loop, this) { Services = services });
        options.ConfigureServices?.Invoke(this, services);
        try
        {
            Services = services.Freeze();
        }
        catch
        {
            services.Dispose();
            Scene.Root.Destroy();
            Backend.Dispose();
            Display.Dispose();
            Renderer.Dispose();
            throw;
        }

        Services.Find<ShmGlobal>()?.Advertise(Renderer.ShmTextureFormats);
        if (Color is { } color && !color.Attach(Services, Scene))
        {
            Log.Info($"color management was asked for and the color manager is absent");
        }

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
        frames.FrameRequested += Scene.RequestFrame;
        Screens = new HostScreens(this);
        Wake = new HostedWakeSource(Loop);
        if (options.Dmabuf)
        {
            Renderer.EglAvailable += OnEglAvailable;
            Renderer.DmabufAvailable += AdvertiseDmabuf;
        }
    }

    private LinuxDmabufGlobal? _dmabuf;

    /// <summary>
    /// Gets the linux-dmabuf global. It is <see langword="null"/> until the renderer reports a dmabuf import path with a render node, and always when <see cref="BasinCompositorOptions.Dmabuf"/> is <see langword="false"/>.
    /// </summary>
    public LinuxDmabufGlobal? Dmabuf => _dmabuf;

    private void OnEglAvailable(HostedEglImport import)
    {
        if (_disposed || _dmabuf is not null || !HostedEglImport.IsSupported)
        {
            return;
        }

        AdvertiseDmabuf(import.Formats, import.RenderNodePath ?? "/dev/dri/renderD128");
    }

    private void AdvertiseDmabuf(DrmFormatSet formats, string node)
    {
        if (_disposed || _dmabuf is not null)
        {
            return;
        }

        if (!File.Exists(node))
        {
            Log.Info($"no render node for dmabuf feedback; the global stays withheld");
            return;
        }

        _dmabuf = new LinuxDmabufGlobal(
            Display,
            Services.Require<ClientBufferRegistry>(),
            formats,
            node,
            compositor: Services.Require<CompositorGlobal>());
        Log.Info($"dmabuf advertised with {formats.Count} format rows on {node}");
    }

    /// <summary>
    /// Gets the set of advertised <c>wl_output</c> globals that describe the host desktop to clients.
    /// </summary>
    public HostScreens Screens { get; }

    /// <summary>
    /// Gets the color-management pack. It is <see langword="null"/> unless <see cref="BasinCompositorOptions.ColorManagement"/> is <see langword="true"/>.
    /// </summary>
    public ColorCapabilityPack? Color { get; }

    internal void NotifyOutputAdvertised(OutputGlobal global, HostedOutput output)
    {
        if (Color is not { } color)
        {
            return;
        }

        Scene.SceneOutput? scene = null;
        foreach (var view in _views)
        {
            if (ReferenceEquals(view.Output, output))
            {
                scene = view.SceneOutput;
                break;
            }
        }

        color.AddOutput(global, output, scene);
    }

    internal void NotifyOutputWithdrawn(OutputGlobal global) => Color?.RemoveOutput(global);

    /// <summary>
    /// Gets the Wayland display that the host owns and disposes.
    /// </summary>
    public WlServerDisplay Display { get; }

    /// <summary>
    /// Gets the name of the bound local socket. It is empty when the display cannot serve local clients.
    /// </summary>
    public string Socket { get; }

    /// <summary>
    /// Gets the event loop over the display. The host never runs it. The consumer dispatches it from its own frame or wake.
    /// </summary>
    public WaylandEventLoop Loop { get; }

    /// <summary>
    /// Gets the backend that creates the hosted outputs.
    /// </summary>
    public HostedBackend Backend { get; }

    /// <summary>
    /// Gets the scene that every view output draws.
    /// </summary>
    public Scene.Scene Scene { get; }

    /// <summary>
    /// Gets the output layout that places the advertised outputs in logical coordinates.
    /// </summary>
    public OutputLayout Layout { get; }

    /// <summary>
    /// Gets the frozen service table.
    /// </summary>
    public BasinServices Services { get; }

    /// <summary>
    /// Gets the xdg-shell global from the desktop pack.
    /// </summary>
    public XdgShell Shell { get; }

    /// <summary>
    /// Gets the seat. Its keyboard starts with the host keymap, the system keymap or a built-in fallback, in that order of preference.
    /// </summary>
    public Seat.Seat Seat { get; }

    /// <summary>
    /// Gets the hosted session that runs the frame tick and tracks the view outputs.
    /// </summary>
    public HostedSession Session { get; }

    /// <summary>
    /// Gets the wake source that reports when the loop has work while no frame runs.
    /// </summary>
    public HostedWakeSource Wake { get; }

    /// <summary>
    /// Gets the renderer that draws into the canvas that the host view lends for each frame.
    /// </summary>
    public HostedRenderer Renderer { get; }

    /// <summary>
    /// Gets the key synthesizer over the seat keyboard. The host also types committed text through it when no text-input client claims the text.
    /// </summary>
    public global::Basin.Seat.KeySynthesizer Keys { get; }

    private void TypeUnclaimed(string text)
    {
        _thread.Assert();
        Keys.Keymap = Seat.Keyboard.Keymap;
        Keys.Layout = Seat.Keyboard.State?.SerializeLayout(Xkb.XkbStateComponent.LayoutEffective) ?? 0;
        Keys.Type(text, (uint)Environment.TickCount);
    }

    /// <summary>
    /// Occurs when <see cref="RequestPointerLock"/> changes <see cref="IsPointerLockRequested"/>. The argument is the new value.
    /// </summary>
    public event Action<bool>? PointerLockRequested;

    /// <summary>
    /// Occurs on each <see cref="RequestPointerWarp"/> call, with the target position in layout coordinates.
    /// </summary>
    public event Action<double, double>? PointerWarpRequested;

    /// <summary>
    /// Gets a value indicating whether the shell asked the host view to lock the host pointer.
    /// </summary>
    public bool IsPointerLockRequested { get; private set; }

    /// <summary>
    /// Asks the host view to lock or unlock the host pointer. A call that does not change the state raises nothing.
    /// </summary>
    /// <param name="locked"><see langword="true"/> to lock the pointer, <see langword="false"/> to release it.</param>
    public void RequestPointerLock(bool locked)
    {
        _thread.Assert();
        if (IsPointerLockRequested == locked)
        {
            return;
        }

        IsPointerLockRequested = locked;
        PointerLockRequested?.Invoke(locked);
    }

    /// <summary>
    /// Asks the host view to move the host pointer. The host only raises <see cref="PointerWarpRequested"/>.
    /// </summary>
    /// <param name="layoutX">The horizontal target in layout coordinates.</param>
    /// <param name="layoutY">The vertical target in layout coordinates.</param>
    public void RequestPointerWarp(double layoutX, double layoutY)
    {
        _thread.Assert();
        PointerWarpRequested?.Invoke(layoutX, layoutY);
    }

    /// <summary>
    /// Cancels every active touch point on the seat and tells the clients.
    /// </summary>
    public void CancelTouch()
    {
        _thread.Assert();
        Seat.Touch.NotifyCancel();
    }

    private bool _suspended;

    /// <summary>
    /// Gets a value indicating whether <see cref="Suspend"/> stopped the host.
    /// </summary>
    public bool IsSuspended => _suspended;

    /// <summary>
    /// Stops all drawing and wake reports, for example when the application goes to the background.
    /// While suspended, <see cref="EnterFrame"/> returns <see langword="false"/> and no view presents. A second call does nothing.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The host is disposed.</exception>
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

    /// <summary>
    /// Undoes <see cref="Suspend"/> and requests a render on every view. A call while not suspended does nothing.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The host is disposed.</exception>
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

    /// <summary>
    /// Occurs after a host view composites a frame. The argument is the total count of frames that the session composited.
    /// </summary>
    public event Action<long>? Composited;

    private string? _screenshotPath;
    private Action<bool>? _screenshotDone;

    /// <summary>
    /// Asks the next frame to write the first view output to a PNG file. A second request before that frame replaces the first, and the first callback then receives <see langword="false"/>.
    /// </summary>
    /// <param name="path">The PNG file to write.</param>
    /// <param name="done">The callback that receives <see langword="true"/> when the file was written.</param>
    public void RequestScreenshot(string path, Action<bool> done)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(done);
        _thread.Assert();
        var replaced = _screenshotDone;
        _screenshotPath = null;
        _screenshotDone = null;
        replaced?.Invoke(false);
        _screenshotPath = path;
        _screenshotDone = done;
    }

    internal bool ScreenshotPending => _screenshotPath is not null;

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

    private readonly Dictionary<Scene.SceneOutput, (byte[] Pixels, int Width, int Height)> _presentedPixels = [];
    private Action<Scene.SceneOutput>? _presented;

    /// <summary>
    /// Gets or sets a value indicating whether each frame reads back the pixels of every view for presented-output diagnostics.
    /// </summary>
    public bool CapturePresented { get; set; }

    IReadOnlyList<Scene.SceneOutput> Scene.IPresentedOutputs.Outputs => [.. _views.Select(static view => view.SceneOutput)];

    event Action<Scene.SceneOutput>? Scene.IPresentedOutputs.Presented
    {
        add => _presented += value;
        remove => _presented -= value;
    }

    bool Scene.IPresentedOutputs.TryReadPresented(
        Scene.SceneOutput output, IRenderer renderer, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? rgba, out int width, out int height)
    {
        if (_presentedPixels.TryGetValue(output, out var presented))
        {
            (rgba, width, height) = presented;
            return true;
        }

        (rgba, width, height) = (null, 0, 0);
        return false;
    }

    void Scene.IPresentedOutputs.AllowPlanes(bool allowed)
    {
    }

    void Scene.IPresentedOutputs.RequestRepaint(Scene.SceneOutput output)
    {
        foreach (var view in _views)
        {
            if (view.SceneOutput == output)
            {
                output.Ring.AddWhole();
                view.RequestRender?.Invoke();
            }
        }
    }

    internal void NotifyPresented(Scene.SceneOutput output, byte[]? pixels, int width, int height)
    {
        if (pixels is not null)
        {
            _presentedPixels[output] = (pixels, width, height);
        }

        _presented?.Invoke(output);
    }

    /// <summary>
    /// Requests a render on every view whose scene output needs a repaint. It does nothing while the host is suspended.
    /// </summary>
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

    /// <summary>
    /// Marks the host keymap as changed. The next <see cref="EnterFrame"/> reloads it, but only when the keyboard follows the host keymap.
    /// </summary>
    public void NotifyHostKeymapChanged()
    {
        if (Seat.Keyboard.KeymapSource is global::Basin.Seat.HostKeymapSource)
        {
            _hostKeymapDirty = true;
        }
    }

    /// <summary>
    /// Opens a frame: it reloads a changed host keymap and begins the session frame. Each successful call must be paired with one <see cref="ExitFrame"/>.
    /// </summary>
    /// <param name="stamp">The host's frame time. A frame with the same stamp as the previous one does not open.</param>
    /// <returns><see langword="true"/> when the frame opened. It is <see langword="false"/> while suspended, while a frame is open, or for a repeated stamp.</returns>
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

    /// <summary>
    /// Closes the frame that <see cref="EnterFrame"/> opened and ends the session frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">No frame is open.</exception>
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

    /// <summary>
    /// Creates an output that a host view draws, with a scene output and a frame target. The output is not advertised to clients.
    /// </summary>
    /// <param name="width">The mode width in pixels.</param>
    /// <param name="height">The mode height in pixels.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="name">The output name, or <see langword="null"/> for the backend's default.</param>
    /// <returns>The new view output. The caller owns it and must dispose it before the host.</returns>
    /// <exception cref="ObjectDisposedException">The host is disposed.</exception>
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

    internal void ForgetView(BasinViewOutput view)
    {
        _views.Remove(view);
        _presentedPixels.Remove(view.SceneOutput);
    }

    internal IReadOnlyList<BasinViewOutput> Views => _views;

    /// <summary>
    /// Disposes the views, the session, the screens, the services, the scene, the backend, the display and the renderer, in that order.
    /// </summary>
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
