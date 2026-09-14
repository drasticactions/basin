using System.Diagnostics;
using Basin.Portal;
using Xunit;

namespace Basin.Tests;

internal sealed class PortalFrontendFixture : IDisposable
{
    private static readonly string[] FrontendCandidates =
    [
        "/usr/lib/xdg-desktop-portal",
        "/usr/libexec/xdg-desktop-portal",
        "/usr/lib/xdg-desktop-portal/xdg-desktop-portal",
    ];

    private readonly string _directory;
    private Process? _frontend;
    private Thread? _reader;

    private PortalFrontendFixture(PrivateBus bus, string directory)
    {
        Bus = bus;
        _directory = directory;
    }

    public PrivateBus Bus { get; }

    public string Address => Bus.Address;

    public static string? FrontendPath { get; } = FindFrontend();

    public static void SkipUnlessAvailable()
    {
        Assert.SkipUnless(PrivateBus.IsAvailable, "dbus-daemon is not installed");
        Assert.SkipUnless(FrontendPath is not null, "xdg-desktop-portal is not installed");
    }

    public static PortalFrontendFixture Start(params string[] interfaces)
    {
        SkipUnlessAvailable();
        var bus = PrivateBus.Start();
        var directory = Path.Combine(Path.GetTempPath(), "basin-portal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var all = interfaces.Length == 0
            ? PortalRegistration.AllInterfaces
            : interfaces.Append("org.freedesktop.impl.portal.Access").Distinct().ToArray();
        File.WriteAllText(Path.Combine(directory, "basin.portal"), PortalRegistration.PortalFile(all, PortalBusTests.BusName));
        File.WriteAllText(Path.Combine(directory, "basin-portals.conf"), PortalRegistration.ConfigFile(all));
        return new PortalFrontendFixture(bus, directory);
    }

    public void StartFrontend(CompositorTestHost host, PortalBus portalBus)
    {
        var info = new ProcessStartInfo(FrontendPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--verbose");
        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = Address;
        info.Environment["XDG_CURRENT_DESKTOP"] = "basin";
        info.Environment["XDG_DESKTOP_PORTAL_DIR"] = _directory;
        info.Environment["XDG_DATA_HOME"] = Path.Combine(_directory, "data");
        info.Environment["XDG_CONFIG_HOME"] = Path.Combine(_directory, "config");
        info.Environment["XDG_CACHE_HOME"] = Path.Combine(_directory, "cache");
        info.Environment.Remove("WAYLAND_DISPLAY");
        info.Environment.Remove("DISPLAY");
        _frontend = Process.Start(info) ?? throw new InvalidOperationException("xdg-desktop-portal did not start");
        _frontend.StandardOutput.BaseStream.Dispose();
        _reader = new Thread(ReadFrontendLog) { IsBackground = true, Name = "xdg-desktop-portal log" };
        _reader.Start();
        PortalBusTests.PumpUntil(host, () => portalBus.FrontendUniqueName is not null || _frontend.HasExited, rounds: 1500);
        Assert.False(_frontend.HasExited, "xdg-desktop-portal exited before owning its name:\n" + Dump());
        PortalBusTests.PumpUntil(host, () => Saw("Providing portal"), rounds: 500);
    }

    public List<string> FrontendLog { get; } = [];

    private void ReadFrontendLog()
    {
        try
        {
            var error = _frontend!.StandardError;
            while (error.ReadLine() is { } line)
            {
                Log(line);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    public bool Saw(string text)
    {
        lock (FrontendLog)
        {
            return FrontendLog.Any(line => line.Contains(text, StringComparison.Ordinal));
        }
    }

    public string Dump()
    {
        lock (FrontendLog)
        {
            return string.Join('\n', FrontendLog);
        }
    }

    private void Log(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (FrontendLog)
        {
            FrontendLog.Add(line);
        }
    }

    public void Dispose()
    {
        if (_frontend is { } frontend)
        {
            if (!frontend.HasExited)
            {
                frontend.Kill();
            }

            frontend.WaitForExit();
            _reader?.Join(2000);
            frontend.StandardError.Dispose();
            frontend.Dispose();
        }

        Bus.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string? FindFrontend()
    {
        foreach (var candidate in FrontendCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
