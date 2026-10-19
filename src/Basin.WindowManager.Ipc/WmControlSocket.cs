using Basin.Capabilities;
using Basin.Ipc;

namespace Basin.WindowManager.Ipc;

public sealed class WmControlSocket : IDisposable
{
    private readonly IpcServer _server;

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

    public string? Path { get; }

    public IpcServer Server => _server;

    public void Dispose() => _server.Dispose();
}
