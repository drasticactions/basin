using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Basin.Diagnostics;

public static class BasinDiagnostics
{
    public const string TraceVariable = "BASIN_TRACE";

    private const int Sigkill = 9;

    private const int MaxTreeDepth = 8;

    private static readonly bool TraceRequested = Environment.GetEnvironmentVariable(TraceVariable) is not null;

    public static bool TraceEnabled => TraceRequested || BasinLog.Level == BasinLogLevel.Trace;

    public static void RouteToStandardError(BasinLogLevel? level = null)
    {
        BasinLog.Level = level ?? (TraceEnabled ? BasinLogLevel.Debug : BasinLogLevel.Warn);
        BasinLog.Sink = new StandardErrorLogSink();
        WaylandDiagnostics.RouteToBasinLog();
    }

    public static Process? StartClient(
        string? command,
        string socket,
        IReadOnlyList<(string Name, string? Value)>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(command);
        info.Environment["WAYLAND_DISPLAY"] = socket;
        info.Environment["XDG_SESSION_TYPE"] = "wayland";
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null)
                {
                    info.Environment.Remove(name);
                }
                else
                {
                    info.Environment[name] = value;
                }
            }
        }

        return Process.Start(info);
    }

    public static void StopClient(Process? process, int timeoutMs = 2000)
    {
        if (process is { HasExited: false })
        {
            if (!TryKillTree(process.Id, 0))
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(timeoutMs);
        }

        process?.Dispose();
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    private static extern int Kill(int pid, int signal);

    private static bool TryKillTree(int pid, int depth)
    {
        var tasks = $"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/task";
        if (!Directory.Exists(tasks))
        {
            return false;
        }

        if (depth < MaxTreeDepth)
        {
            foreach (var task in Directory.EnumerateDirectories(tasks))
            {
                var children = Path.Combine(task, "children");
                if (!File.Exists(children))
                {
                    return false;
                }

                string listed;
                try
                {
                    listed = File.ReadAllText(children);
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var entry in listed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(entry, CultureInfo.InvariantCulture, out var child))
                    {
                        TryKillTree(child, depth + 1);
                    }
                }
            }
        }

        Kill(pid, Sigkill);
        return true;
    }
}
