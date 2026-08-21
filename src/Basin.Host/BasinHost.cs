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

    public ISession? Session { get; }

    public DrmBackend? Drm { get; }

    public WaylandBackend? Parent { get; }

    public HeadlessBackend? Headless { get; }

    public string? RenderNodePath => Drm?.RenderNodePath;

    public static BasinHost Create(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var display = options.Transport == HostTransport.Managed
            ? WlServerDisplay.Create(new ManagedTransport())
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
                    loop, session, options.DrmDevice ?? Environment.GetEnvironmentVariable("BASIN_DRM_DEVICE"));
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

        return new BasinHost(display, socket, loop, session, drm, parent, headless);
    }

    public BasinServices CreateServices(IFrameClock? frames = null)
    {
        var services = new BasinServices(Display, Loop);
        services.UseDefault(frames ?? new FrameClock());
        return services;
    }

    public bool EnableOutput(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var modeset = new OutputState();
        modeset.SetEnabled(true);
        if (output is DrmOutput card)
        {
            modeset.SetMode(card.PreferredMode);
        }

        return output.Commit(modeset);
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
