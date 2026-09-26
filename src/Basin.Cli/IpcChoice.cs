using Basin.Ipc;

namespace Basin.Cli;

public sealed record IpcChoice(bool Listen, string? Path)
{
    public static IpcChoice Off { get; } = new(false, null);

    public IpcServer Attach(
        ICompositorEventLoop loop, BasinServices services, string? socketName, IpcSessionInfo info) =>
        new(loop, services, info with { WaylandSocket = socketName ?? info.WaylandSocket }, Path, Listen);

    public IpcServer? AttachIfListening(
        ICompositorEventLoop loop, BasinServices services, string? socketName, IpcSessionInfo info) =>
        Listen ? Attach(loop, services, socketName, info) : null;

    public IpcServer? Serve(
        ICompositorEventLoop loop, BasinServices services, string? socketName, IpcSessionInfo info)
    {
        var server = AttachIfListening(loop, services, socketName, info);
        server?.Start();
        return server;
    }
}
