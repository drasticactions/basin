using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcSessionMethods
{
    public static void Register(IpcServer server)
    {
        var session = server.Session;
        server.Methods.RegisterLibrary(IpcMethodNames.SessionDescribe, (ref IpcParams _, IpcReply reply) =>
            reply.Write(
                new IpcSessionDescription(
                    session.Compositor,
                    session.Backend,
                    session.Renderer,
                    session.WaylandSocket,
                    session.XwaylandDisplay?.Invoke(),
                    server.Path,
                    server.Services.Find<IOutputSet>()?.Outputs.Count,
                    Environment.ProcessId),
                IpcJsonContext.Default.IpcSessionDescription));

        if (session.Quit is { } quit)
        {
            server.Methods.RegisterLibrary(IpcMethodNames.SessionQuit, (ref IpcParams _, IpcReply reply) =>
            {
                quit();
                IpcWrite.Empty(reply);
            });
        }
    }
}
