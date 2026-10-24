using Basin.Capabilities;
using Basin.Ipc;

namespace Basin.WindowManager.Ipc;

/// <summary>
/// A basin control socket for a window manager. It serves the window-part diagnostics on the manager's own loop.
/// </summary>
public sealed class WmControlSocket : IDisposable
{
    private readonly IpcServer _server;

    /// <summary>
    /// Initializes a new instance of the <see cref="WmControlSocket"/> class and starts the server.
    /// </summary>
    /// <param name="loop">The manager's loop, usually <see cref="RiverWindowManager.Loop"/>.</param>
    /// <param name="name">The manager name that the session reports. With <c>WAYLAND_DISPLAY</c> set, the socket name adds the display name to it.</param>
    /// <param name="parts">The source of each decorated window's frame parts.</param>
    /// <param name="quit">The action for a quit request, or <see langword="null"/> to refuse quit.</param>
    /// <param name="path">The socket path, or <see langword="null"/> for the default under <c>XDG_RUNTIME_DIR</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> or <paramref name="parts"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public WmControlSocket(IWmEventLoop loop, string name, IWindowPartsSource parts, Action? quit = null, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(parts);
        var adapter = new WmIpcLoop(loop);
        var services = new BasinServices(adapter).Use(parts).Freeze();
        var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        Path = path ?? IpcProtocol.SocketPath(
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
            string.IsNullOrEmpty(display) ? name : $"{name}-{System.IO.Path.GetFileName(display)}");
        _server = new IpcServer(adapter, services, new IpcSessionInfo { Compositor = name, Quit = quit }, Path, listen: Path is not null);
        _server.Start();
    }

    /// <summary>
    /// Gets the socket path, or <see langword="null"/> when no path was given and <c>XDG_RUNTIME_DIR</c> is not set. The server does not listen then.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// Gets the server, which this object owns.
    /// </summary>
    public IpcServer Server => _server;

    /// <summary>
    /// Stops the server and closes the socket.
    /// </summary>
    public void Dispose() => _server.Dispose();
}
