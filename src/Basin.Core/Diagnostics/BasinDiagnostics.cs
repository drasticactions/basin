using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Basin.Diagnostics;

public static class BasinDiagnostics
{
    public const string TraceVariable = "BASIN_TRACE";

    private const int Sigkill = 9;

    private const int MaxTreeDepth = 8;

    public static bool TraceEnabled { get; } = Environment.GetEnvironmentVariable(TraceVariable) is not null;

    public static void RouteToStandardError(BasinLogLevel? level = null)
    {
        BasinLog.Level = level ?? (TraceEnabled ? BasinLogLevel.Debug : BasinLogLevel.Warn);
        BasinLog.Sink = (severity, message) => Console.Error.WriteLine($"[{severity}] {message}");
        WaylandDiagnostics.RouteToBasinLog();
    }

    public static Process? StartClient(string? command, string socket)
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
