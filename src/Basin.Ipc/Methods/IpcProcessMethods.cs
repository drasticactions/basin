namespace Basin.Ipc;

internal static class IpcProcessMethods
{
    private const int DefaultGraceMs = 2000;
    private const int MaxGraceMs = 600_000;
    private const int DefaultLines = 50;
    private const int MaxLines = 10_000;
    private const int LogTailBytes = 1024 * 1024;

    public static void Register(IpcServer server)
    {
        var tracker = server.Processes;
        var methods = server.Methods;
        methods.RegisterLibrary(IpcMethodNames.ProcessSpawn, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcSpawnParams) is not { } request)
            {
                return;
            }

            var launch = new IpcLaunch(request.Argv)
            {
                Env = request.Env,
                UnsetEnv = request.UnsetEnv,
                Cwd = request.Cwd,
                LogPath = request.Log,
            };
            if (request.Argv.Count == 0 || request.Argv[0].Length == 0)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'argv' names at least a program");
                return;
            }

            if (request.Cwd is { } cwd && !Path.IsPathRooted(cwd))
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'cwd' is absolute");
                return;
            }

            if (request.Log is { } log && !Path.IsPathRooted(log))
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'log' is absolute");
                return;
            }

            if (tracker.Spawn(launch, out var error) is not { } process)
            {
                reply.Error(IpcErrorCodes.Failed, error ?? "the process did not start");
                return;
            }

            reply.Write(new IpcSpawnResult(process.Pid, process.LaunchId), IpcJsonContext.Default.IpcSpawnResult);
        });

        methods.RegisterLibrary(IpcMethodNames.ProcessList, (ref IpcParams _, IpcReply reply) =>
        {
            var list = new IpcProcess[tracker.Processes.Count];
            for (var i = 0; i < list.Length; i++)
            {
                list[i] = Describe(tracker.Processes[i]);
            }

            reply.Write(new IpcProcessList(list), IpcJsonContext.Default.IpcProcessList);
        });

        methods.RegisterLibrary(IpcMethodNames.ProcessKill, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcProcessKillParams) is not { } request)
            {
                return;
            }

            var grace = request.GraceMs ?? DefaultGraceMs;
            if (grace < 0 || grace > MaxGraceMs)
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'grace_ms' is between 0 and {MaxGraceMs}");
                return;
            }

            if (tracker.Find(request.LaunchId) is null)
            {
                reply.Error(IpcErrorCodes.NotFound, $"no launch {request.LaunchId}");
                return;
            }

            if (!tracker.Kill(request.LaunchId, (int)grace))
            {
                reply.Error(IpcErrorCodes.Refused, $"launch {request.LaunchId} has no process left to signal");
                return;
            }

            IpcWrite.Empty(reply);
        });

        methods.RegisterLibrary(IpcMethodNames.ProcessLog, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcProcessLogParams) is not { } request)
            {
                return;
            }

            var count = request.Lines ?? DefaultLines;
            if (count <= 0 || count > MaxLines)
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'lines' is between 1 and {MaxLines}");
                return;
            }

            if (tracker.Find(request.LaunchId) is not { } process)
            {
                reply.Error(IpcErrorCodes.NotFound, $"no launch {request.LaunchId}");
                return;
            }

            if (process.LogPath is not { } path)
            {
                reply.Error(IpcErrorCodes.NotFound, $"launch {request.LaunchId} has no log");
                return;
            }

            string[] lines;
            try
            {
                lines = Tail(path, (int)count);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                reply.Error(IpcErrorCodes.Failed, $"cannot read '{path}': {exception.Message}");
                return;
            }

            reply.Write(new IpcProcessLog(process.LaunchId, path, lines), IpcJsonContext.Default.IpcProcessLog);
        });

        if (methods.IsOmitted(IpcMethodNames.ProcessSpawn) && methods.IsOmitted(IpcMethodNames.ProcessList)
            && methods.IsOmitted(IpcMethodNames.ProcessKill) && methods.IsOmitted(IpcMethodNames.ProcessLog))
        {
            return;
        }

        server.Events.DeclareLibrary(IpcEventNames.ProcessExited);
        tracker.Exited += process => server.Events.Emit(
            IpcEventNames.ProcessExited,
            new IpcProcessExited(process.LaunchId, process.Pid, process.ExitCode, process.Signal),
            IpcJsonContext.Default.IpcProcessExited);
    }

    internal static IpcProcess Describe(IpcTrackedProcess process) =>
        new(process.LaunchId, process.Pid, process.ArgvArray, process.IsRunning, process.ExitCode, process.Signal, process.LogPath);

    private static string[] Tail(string path, int count)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - LogTailBytes);
        stream.Position = start;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var text = reader.ReadToEnd();
        var lines = text.Split('\n');
        var end = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var first = start > 0 ? 1 : 0;
        var from = Math.Max(first, end - count);
        return from >= end ? [] : lines[from..end];
    }
}
