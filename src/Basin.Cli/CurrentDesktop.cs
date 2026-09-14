using System.Diagnostics;
using Basin.Diagnostics;

namespace Basin.Cli;

public static class CurrentDesktop
{
    private static readonly BasinLogger Log = BasinLog.For("desktop");

    public static string Export(string name, string? display = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var current = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        var next = Append(current, name);
        var variables = new List<string>();
        if (!string.Equals(current, next, StringComparison.Ordinal))
        {
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", next);
            variables.Add("XDG_CURRENT_DESKTOP");
        }

        if (!string.IsNullOrEmpty(display))
        {
            var outer = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            if (string.IsNullOrEmpty(outer))
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", display);
                variables.Add("WAYLAND_DISPLAY");
            }
            else
            {
                Log.Info($"nested inside {outer}: WAYLAND_DISPLAY stays the outer session's in the activation environment; a dbus-activated portal needs --socket {display}");
            }
        }

        if (variables.Count == 0)
        {
            return next;
        }

        try
        {
            var info = new ProcessStartInfo("dbus-update-activation-environment")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            info.ArgumentList.Add("--systemd");
            foreach (var variable in variables)
            {
                info.ArgumentList.Add(variable);
            }

            using var update = Process.Start(info);
            update?.WaitForExit(2000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Info($"dbus-update-activation-environment did not run: {e.Message}; a dbus-activated portal frontend may not see {string.Join(" ", variables)}");
        }

        return next;
    }

    public static string Append(string? current, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (string.IsNullOrEmpty(current))
        {
            return name;
        }

        foreach (var part in current.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, name, StringComparison.Ordinal))
            {
                return current;
            }
        }

        return current + ":" + name;
    }
}
