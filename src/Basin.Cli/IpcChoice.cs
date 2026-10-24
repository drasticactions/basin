using Basin.Ipc;

namespace Basin.Cli;

/// <summary>
/// Holds the parsed value of the <c>--ipc</c> option.
/// </summary>
/// <param name="Listen">Whether the compositor binds a control socket.</param>
/// <param name="Path">The control socket path, or <see langword="null"/> for the default path in <c>XDG_RUNTIME_DIR</c>.</param>
public sealed record IpcChoice(bool Listen, string? Path)
{
    /// <summary>
    /// Gets the choice that binds no control socket.
    /// </summary>
    public static IpcChoice Off { get; } = new(false, null);

    /// <summary>
    /// Creates a control server even when <see cref="Listen"/> is <see langword="false"/>. Such a server binds no socket but can serve the stdin line front. The caller must start and dispose the server.
    /// </summary>
    /// <param name="loop">The compositor loop that the server runs on.</param>
    /// <param name="services">The services that the IPC methods read.</param>
    /// <param name="socketName">The compositor's Wayland socket name, or <see langword="null"/> to keep the one in <paramref name="info"/>.</param>
    /// <param name="info">The session information that the server reports.</param>
    /// <returns>The server, not started.</returns>
    public IpcServer Attach(
        ICompositorEventLoop loop, BasinServices services, string? socketName, IpcSessionInfo info) =>
        new(loop, services, info with { WaylandSocket = socketName ?? info.WaylandSocket }, Path, Listen);

    /// <summary>
    /// Creates a control server only when <see cref="Listen"/> is <see langword="true"/>. The caller must start and dispose the server.
    /// </summary>
    /// <param name="loop">The compositor loop that the server runs on.</param>
    /// <param name="services">The services that the IPC methods read.</param>
    /// <param name="socketName">The compositor's Wayland socket name, or <see langword="null"/> to keep the one in <paramref name="info"/>.</param>
    /// <param name="info">The session information that the server reports.</param>
    /// <returns>The server, not started, or <see langword="null"/> when the choice is off.</returns>
    public IpcServer? AttachIfListening(
        ICompositorEventLoop loop, BasinServices services, string? socketName, IpcSessionInfo info) =>
        Listen ? Attach(loop, services, socketName, info) : null;

    /// <summary>
    /// Creates and starts a control server when <see cref="Listen"/> is <see langword="true"/>. The caller must dispose the server.
    /// </summary>
    /// <param name="loop">The compositor loop that the server runs on.</param>
    /// <param name="services">The services that the IPC methods read.</param>
    /// <param name="socketName">The compositor's Wayland socket name, or <see langword="null"/> to keep the one in <paramref name="info"/>.</param>
    /// <param name="info">The session information that the server reports.</param>
    /// <returns>The started server, or <see langword="null"/> when the choice is off.</returns>
    public IpcServer? Serve(
        ICompositorEventLoop loop, BasinServices services, string? socketName, IpcSessionInfo info)
    {
        var server = AttachIfListening(loop, services, socketName, info);
        server?.Start();
        return server;
    }
}
