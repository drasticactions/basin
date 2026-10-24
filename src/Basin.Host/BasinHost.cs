using Basin.Backend.Drm;
using Basin.Backend.Headless;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Session;
using Wayland.Server;

namespace Basin.Host;

/// <summary>
/// Owns the Wayland display, its socket, the event loop and the backend of a compositor. <see cref="Create"/> builds it from <see cref="HostOptions"/>.
/// </summary>
public sealed class BasinHost : IDisposable
{
    private bool _disposed;

    private BasinHost(
        WlServerDisplay display,
        string socket,
        WaylandEventLoop loop,
        ISession? session,
        DrmBackend? drm,
        WaylandBackend? parent,
        HeadlessBackend? headless)
    {
        Display = display;
        Socket = socket;
        Loop = loop;
        Session = session;
        Drm = drm;
        Parent = parent;
        Headless = headless;
    }

    /// <summary>
    /// Gets the Wayland display.
    /// </summary>
    public WlServerDisplay Display { get; }

    /// <summary>
    /// Gets the name of the listening socket. It is empty when the socket is inherited or the transport has no local socket.
    /// </summary>
    public string Socket { get; }

    /// <summary>
    /// Gets the event loop of the display.
    /// </summary>
    public WaylandEventLoop Loop { get; }

    /// <summary>
    /// Gets the per-output sets of presented surfaces. <see cref="CreateServices"/> registers it as a default.
    /// </summary>
    public PresentedSets PresentedSets { get; } = new();

    /// <summary>
    /// Gets the services that <see cref="CreateServices"/> created, or <see langword="null"/> before the call.
    /// </summary>
    public BasinServices? Services { get; private set; }

    /// <summary>
    /// Gets or sets the file descriptor that gets <c>READY=1</c> at <see cref="NotifyReady"/>, or <see langword="null"/> for none. The host closes it after the write.
    /// </summary>
    public int? ReadyFd { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="NotifyReady"/> ran.
    /// </summary>
    public bool Ready { get; private set; }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, byte[] buffer, nuint count);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    /// <summary>
    /// Reports readiness once: it writes <c>READY=1</c> to <see cref="ReadyFd"/> and closes it, and sends <c>READY=1</c> to the systemd <c>NOTIFY_SOCKET</c> when it is set. Later calls do nothing.
    /// </summary>
    public void NotifyReady()
    {
        if (Ready)
        {
            return;
        }

        Ready = true;
        var message = "READY=1\n"u8.ToArray();
        if (ReadyFd is { } fd && fd >= 0)
        {
            _ = write(fd, message, (nuint)message.Length);
            _ = close(fd);
        }

        if (Environment.GetEnvironmentVariable("NOTIFY_SOCKET") is { Length: > 0 } path)
        {
            try
            {
                using var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Unspecified);
                var endpoint = new System.Net.Sockets.UnixDomainSocketEndPoint(path[0] == '@' ? "\0" + path[1..] : path);
                socket.SendTo(message.AsSpan(0, message.Length - 1), endpoint);
            }
            catch (System.Net.Sockets.SocketException)
            {
            }
        }
    }

    /// <summary>
    /// Gets the seat session, or <see langword="null"/> when the backend is not DRM.
    /// </summary>
    public ISession? Session { get; }

    /// <summary>
    /// Gets the DRM backend, or <see langword="null"/> when the backend is not DRM.
    /// </summary>
    public DrmBackend? Drm { get; }

    /// <summary>
    /// Gets a value indicating whether the DRM card was named by <see cref="HostOptions.DrmDevice"/> or <c>BASIN_DRM_DEVICE</c>, not chosen by the backend.
    /// </summary>
    public bool DrmDevicePinned { get; private init; }

    /// <summary>
    /// Gets the handoff policy that <see cref="Create"/> built from the options.
    /// </summary>
    public HandoffPolicy Handoff { get; private init; } = new(HandoffMode.Auto);

    /// <summary>
    /// Occurs during disposal after a retain sequence, once for each DRM output. The argument tells whether the backend retained the output's last frame or why it refused.
    /// </summary>
    public event Action<HandoffRetention>? HandoffRetained;

    /// <summary>
    /// Gets the nested backend, or <see langword="null"/> when the backend is not nested.
    /// </summary>
    public WaylandBackend? Parent { get; }

    /// <summary>
    /// Gets the headless backend, or <see langword="null"/> when the backend is not headless.
    /// </summary>
    public HeadlessBackend? Headless { get; }

    /// <summary>
    /// Gets the render node of the DRM card, or <see langword="null"/> when the backend is not DRM or the card has none.
    /// </summary>
    public string? RenderNodePath => Drm?.RenderNodePath;

    /// <summary>
    /// Creates the display and its socket, and starts the backend. A DRM backend opens a seat session and inherits the lit state unless the handoff mode is off.
    /// </summary>
    /// <param name="options">The options to build the host from.</param>
    /// <returns>The host. The caller must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static BasinHost Create(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var display = options.Transport == HostTransport.Managed
            ? WlServerDisplay.Create(new ManagedTransport(new ManagedTransportOptions { LocalSocket = PlatformFacts.HasLocalClients }))
            : WlServerDisplay.Create();
        string socket;
        if (options.SocketFd >= 0)
        {
            display.AddSocketFd(options.SocketFd);
            socket = string.Empty;
        }
        else if (display.SupportsLocalSocket)
        {
            socket = display.AddSocketAuto();
        }
        else
        {
            socket = string.Empty;
        }

        var loop = new WaylandEventLoop(display);
        ISession? session = null;
        DrmBackend? drm = null;
        var pinned = options.DrmDevice ?? Environment.GetEnvironmentVariable("BASIN_DRM_DEVICE");
        WaylandBackend? parent = null;
        HeadlessBackend? headless = null;
        switch (options.Backend)
        {
            case HostBackend.Drm:
                session = SeatdSession.Open(loop);
                drm = new DrmBackend(
                    loop, session, pinned)
                {
                    Inherit = options.Handoff != HandoffMode.Off,
                };
                drm.Start();
                break;

            case HostBackend.Nested:
                parent = new WaylandBackend(loop);
                parent.Start();
                break;

            default:
                headless = new HeadlessBackend(loop);
                break;
        }

        var host = new BasinHost(display, socket, loop, session, drm, parent, headless)
        {
            Handoff = new HandoffPolicy(options.Handoff, options.Greeter, options.Greetd),
            DrmDevicePinned = drm is not null && !string.IsNullOrEmpty(pinned),
        };
        if (drm is not null)
        {
            drm.HandoffRetained += retention => host.HandoffRetained?.Invoke(retention);
        }

        return host;
    }

    /// <summary>
    /// Creates the service table and seeds the defaults: the frame clock, <see cref="PresentedSets"/>, the client directory and the scene diagnostics. Call it once.
    /// </summary>
    /// <param name="frames">The frame clock to register, or <see langword="null"/> for a new default clock.</param>
    /// <returns>The services, also kept in <see cref="Services"/>.</returns>
    public BasinServices CreateServices(IFrameClock? frames = null)
    {
        var services = new BasinServices(Display, Loop);
        services.UseDefault(frames ?? new FrameClock());
        services.UseDefault(PresentedSets);
        services.UseDefault<IClientDirectory>(new Basin.Capabilities.Defaults.DisplayClientDirectory(Display));
        Services = services;
        SeedDiagnostics();
        return services;
    }

    /// <summary>
    /// Gets the first output driver that was created for this host, or <see langword="null"/> before one exists.
    /// </summary>
    public OutputDriver? Driver { get; private set; }

    internal void AttachDriver(OutputDriver driver) => Driver ??= driver;

    private void SeedDiagnostics()
    {
        if (Services is { IsFrozen: false } services)
        {
            services.SeedDefault<ISceneDiagnostics>(() => new HostSceneDiagnostics(this, services));
        }
    }

    /// <summary>
    /// Enables an output in one commit. A DRM output gets its start mode, which is the inherited mode when one matches.
    /// </summary>
    /// <param name="output">The output to enable.</param>
    /// <returns><see langword="true"/> when the commit succeeded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public bool EnableOutput(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var modeset = new OutputState();
        modeset.SetEnabled(true);
        if (output is DrmOutput card)
        {
            modeset.SetMode(card.StartMode);
        }

        return output.Commit(modeset);
    }

    /// <summary>
    /// Destroys every connected client.
    /// </summary>
    public void DisconnectClients()
    {
        foreach (var client in Display.Clients)
        {
            if (!client.IsDestroyed)
            {
                client.Destroy();
            }
        }
    }

    /// <summary>
    /// Disposes the backends, the session and the display, in that order. A DRM backend retains or restores its outputs here, as its exit mode says.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Drm?.Dispose();
        Parent?.Dispose();
        Headless?.Dispose();
        Session?.Dispose();
        Display.Dispose();
    }
}
