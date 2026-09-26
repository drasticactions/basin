namespace Basin.Ipc;

internal static class IpcProcessMethods
{
    public static void Register(IpcServer server)
    {
        server.Methods.RegisterLibrary(IpcMethodNames.ProcessSpawn, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcSpawnParams) is not { } request)
            {
                return;
            }

            var argv = request.Argv;
            var cwd = request.Cwd;

            if (argv.Count == 0 || argv[0].Length == 0)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'argv' names at least a program");
                return;
            }

            if (cwd is not null && !Path.IsPathRooted(cwd))
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'cwd' is absolute");
                return;
            }

            var pid = IpcSpawn.Spawn(server, [.. argv], request.Env, cwd, out var error);
            if (pid < 0)
            {
                reply.Error(IpcErrorCodes.Failed, error ?? "the process did not start");
                return;
            }

            reply.Write(new IpcSpawnResult(pid), IpcJsonContext.Default.IpcSpawnResult);
        });
    }
}
