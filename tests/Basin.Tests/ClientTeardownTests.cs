using System.Globalization;
using Xunit;

namespace Basin.Tests;

public sealed class ClientTeardownTests
{
    [Fact]
    public void Stopping_a_client_kills_what_the_client_spawned()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the tracker reaps through a pidfd, which only Linux has");

        var marker = Path.Combine(Path.GetTempPath(), $"basin-tree-{Environment.ProcessId}-{Environment.TickCount64}");
        using var host = new CompositorTestHost();
        try
        {
            using var tracker = new ProcessTracker(host.Loop, () => ProcessTracker.SessionEnvironment("wayland-not-connected", null, null));
            var client = tracker.SpawnShell($"sleep 300 & echo $! > '{marker}'; wait", out var error);
            Assert.True(client is not null, error);

            var spawned = WaitForMarker(marker);
            Assert.True(spawned > 0, "the client never reported the process it spawned");
            Assert.True(Alive(spawned), "the spawned process was not running before teardown");
            Assert.True(tracker.Owns(client!.Pid));

            tracker.TerminateAll(TimeSpan.FromSeconds(2));

            Assert.True(WaitForDeath(spawned), "the spawned process outlived the client it came from");
            Assert.False(tracker.Owns(client.Pid));
        }
        finally
        {
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
    }

    [Fact]
    public void A_spawned_client_inherits_no_descriptor_beyond_its_standard_three()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the check reads /proc/self/fd");

        var marker = Path.Combine(Path.GetTempPath(), $"basin-fds-{Environment.ProcessId}-{Environment.TickCount64}");
        var leaked = OpenInheritable("/dev/null");
        Assert.True(leaked > 2, "could not open a descriptor without close-on-exec");
        using var host = new CompositorTestHost();
        try
        {
            using var tracker = new ProcessTracker(host.Loop, () => ProcessTracker.SessionEnvironment("wayland-not-connected", null, null));
            var client = tracker.SpawnShell($"ls /proc/self/fd > '{marker}.tmp'; mv '{marker}.tmp' '{marker}'", out var error);
            Assert.True(client is not null, error);
            for (var i = 0; i < 200 && !File.Exists(marker); i++)
            {
                Thread.Sleep(20);
            }

            var fds = File.ReadAllLines(marker).Select(line => int.Parse(line, CultureInfo.InvariantCulture)).ToList();
            Assert.DoesNotContain(leaked, fds);
            Assert.Contains(0, fds);
            tracker.TerminateAll(TimeSpan.FromSeconds(1));
        }
        finally
        {
            _ = CloseDescriptor(leaked);
            File.Delete(marker);
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenInheritable(string path, int flags = 0);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close")]
    private static extern int CloseDescriptor(int fd);

    private static int WaitForMarker(string marker)
    {
        for (var i = 0; i < 200; i++)
        {
            if (File.Exists(marker))
            {
                var text = File.ReadAllText(marker).Trim();
                if (int.TryParse(text, CultureInfo.InvariantCulture, out var pid))
                {
                    return pid;
                }
            }

            Thread.Sleep(20);
        }

        return 0;
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SignalProcess(int pid, int signal);

    private static bool Alive(int pid)
    {
        if (!OperatingSystem.IsLinux())
        {
            return SignalProcess(pid, 0) == 0;
        }

        var stat = $"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/stat";
        if (!File.Exists(stat))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(stat);
            var close = text.LastIndexOf(')');
            return close < 0 || !text.AsSpan(close).TrimStart(") ").StartsWith("Z");
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool WaitForDeath(int pid)
    {
        for (var i = 0; i < 200; i++)
        {
            if (!Alive(pid))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }
}
