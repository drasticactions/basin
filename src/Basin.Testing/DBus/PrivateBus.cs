using System.Diagnostics;
using Xunit;

namespace Basin.Testing;

/// <summary>
/// A private D-Bus session daemon for one test. It never touches the desktop session bus.
/// </summary>
public sealed class PrivateBus : IDisposable
{
    private readonly Process _daemon;
    private readonly string _runtime;

    private PrivateBus(Process daemon, string address, string runtime)
    {
        _daemon = daemon;
        Address = address;
        _runtime = runtime;
    }

    /// <summary>
    /// Gets the bus address that clients connect to.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets a value indicating whether dbus-daemon is on the PATH.
    /// </summary>
    public static bool IsAvailable { get; } = OnPath("dbus-daemon");

    /// <summary>
    /// Tells if a program is in a directory of the PATH.
    /// </summary>
    /// <param name="program">The file name of the program.</param>
    /// <returns><see langword="true"/> when the program exists on the PATH.</returns>
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

    /// <summary>
    /// Starts a session daemon. The test is skipped when dbus-daemon is not installed.
    /// The daemon gets its own runtime directory, so a service that it activates, such as the AT-SPI bus launcher, cannot replace a socket of the desktop session.
    /// </summary>
    /// <returns>The running bus.</returns>
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
        info.Environment.Remove("AT_SPI_BUS_ADDRESS");
        var runtime = Directory.CreateTempSubdirectory("basin-bus-").FullName;
        File.SetUnixFileMode(runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        info.Environment["XDG_RUNTIME_DIR"] = runtime;
        var daemon = Process.Start(info) ?? throw new InvalidOperationException("dbus-daemon did not start");
        var address = daemon.StandardOutput.ReadLine();
        if (string.IsNullOrEmpty(address))
        {
            daemon.Kill();
            Directory.Delete(runtime, recursive: true);
            throw new InvalidOperationException("dbus-daemon printed no address");
        }

        daemon.StandardError.BaseStream.Dispose();
        return new PrivateBus(daemon, address, runtime);
    }

    /// <summary>
    /// Stops the daemon.
    /// </summary>
    public void Dispose()
    {
        if (!_daemon.HasExited)
        {
            _daemon.Kill();
            _daemon.WaitForExit(2000);
        }

        _daemon.StandardOutput.Dispose();
        _daemon.Dispose();
        try
        {
            Directory.Delete(_runtime, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
