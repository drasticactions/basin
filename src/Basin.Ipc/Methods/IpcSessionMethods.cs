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

        if (session.Settle is { } settle)
        {
            server.Methods.RegisterLibrary(IpcMethodNames.SessionSettle, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcSettleParams) is not { } request)
                {
                    return;
                }

                var timeout = request.TimeoutMs ?? DefaultSettleMs;
                if (timeout <= 0 || timeout > MaxSettleMs)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, $"'timeout_ms' must be between 1 and {MaxSettleMs}.");
                    return;
                }

                if (settle.IsSettled)
                {
                    reply.Write(new IpcSettleResult(settle.Presented), IpcJsonContext.Default.IpcSettleResult);
                    return;
                }

                new IpcSettleWait(server, settle, reply.Defer()).Start((int)timeout);
            });
        }
    }

    private const int DefaultSettleMs = 30_000;

    private const int MaxSettleMs = 3_600_000;
}
