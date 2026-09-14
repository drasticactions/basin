using System.Diagnostics;
using Xunit;

namespace Basin.Tests;

internal sealed class PrivateBus : IDisposable
{
    private readonly Process _daemon;

    private PrivateBus(Process daemon, string address)
    {
        _daemon = daemon;
        Address = address;
    }

    public string Address { get; }

    public static bool IsAvailable { get; } = OnPath("dbus-daemon");

    public static bool OnPath(string program)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(directory, program)))
            {
                return true;
            }
        }

        return false;
    }

    public static PrivateBus Start()
    {
        Assert.SkipUnless(IsAvailable, "dbus-daemon is not installed");
        var info = new ProcessStartInfo("dbus-daemon")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("--session");
        info.ArgumentList.Add("--print-address=1");
        info.ArgumentList.Add("--nofork");
        info.ArgumentList.Add("--nopidfile");
        info.Environment.Remove("DBUS_SESSION_BUS_ADDRESS");
        var daemon = Process.Start(info) ?? throw new InvalidOperationException("dbus-daemon did not start");
        var address = daemon.StandardOutput.ReadLine();
        if (string.IsNullOrEmpty(address))
        {
            daemon.Kill();
            throw new InvalidOperationException("dbus-daemon printed no address");
        }

        daemon.StandardError.BaseStream.Dispose();
        return new PrivateBus(daemon, address);
    }

    public void Dispose()
    {
        if (!_daemon.HasExited)
        {
            _daemon.Kill();
            _daemon.WaitForExit(2000);
        }

        _daemon.StandardOutput.Dispose();
        _daemon.Dispose();
    }
}
