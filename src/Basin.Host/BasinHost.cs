using Basin.Backend.Drm;
using Basin.Backend.Headless;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Session;
using Wayland.Server;

namespace Basin.Host;

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

    public WlServerDisplay Display { get; }

    public string Socket { get; }

    public WaylandEventLoop Loop { get; }

    public PresentedSets PresentedSets { get; } = new();

    public BasinServices? Services { get; private set; }

    public int? ReadyFd { get; set; }

    public bool Ready { get; private set; }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, byte[] buffer, nuint count);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

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

    public ISession? Session { get; }

    public DrmBackend? Drm { get; }

    public HandoffPolicy Handoff { get; private init; } = new(HandoffMode.Auto);

    public event Action<HandoffRetention>? HandoffRetained;

    public WaylandBackend? Parent { get; }

    public HeadlessBackend? Headless { get; }

    public string? RenderNodePath => Drm?.RenderNodePath;

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
        WaylandBackend? parent = null;
        HeadlessBackend? headless = null;
        switch (options.Backend)
        {
            case HostBackend.Drm:
                session = SeatdSession.Open(loop);
                drm = new DrmBackend(
                    loop, session, options.DrmDevice ?? Environment.GetEnvironmentVariable("BASIN_DRM_DEVICE"))
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
        };
        if (drm is not null)
        {
            drm.HandoffRetained += retention => host.HandoffRetained?.Invoke(retention);
        }

        return host;
    }

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

    public OutputDriver? Driver { get; private set; }

    internal void AttachDriver(OutputDriver driver) => Driver ??= driver;

    private void SeedDiagnostics()
    {
        if (Services is { IsFrozen: false } services)
        {
            services.SeedDefault<ISceneDiagnostics>(() => new HostSceneDiagnostics(this, services));
        }
    }

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
