using System.Diagnostics;
using Basin.Session;
using Drm;
using Xunit;

namespace Basin.Tests;

public sealed class DrmHandoffProcessTests
{
    private static readonly string? VkmsNode = VkmsCard.Find();

    private static readonly string Dam = Path.Combine(AppContext.BaseDirectory, "dam");

    private static void SkipUnlessRunnable()
    {
        Assert.SkipWhen(VkmsNode is null, "no vkms card node");
        Assert.SkipWhen(!File.Exists("/run/seatd.sock"), "no seatd socket; dam takes the card through libseat");
        Assert.SkipWhen(!File.Exists(Dam), "dam was not copied beside the tests");
    }

    private sealed class Holder : IDisposable
    {
        private readonly DirectSession _session = new();
        private readonly ISessionDevice _device;

        public Holder()
        {
            _device = _session.OpenDevice(VkmsNode!);
            using var drm = DrmDevice.FromFd(_device.FileDescriptor, ownsFd: false);
            try
            {
                drm.DropMaster();
            }
            catch (DrmException)
            {
            }
        }

        public void Dispose()
        {
            _device.Dispose();
            _session.Dispose();
        }
    }

    private static List<string> RunDam(params string[] args)
    {
        var start = new ProcessStartInfo(Dam)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var arg in (string[])["--backend", "drm", "--renderer", "pixman", "--frames", "20", "--ipc", "false", .. args])
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["BASIN_DRM_DEVICE"] = VkmsNode;
        start.Environment["LIBSEAT_BACKEND"] = "seatd";
        start.Environment.Remove("WAYLAND_DISPLAY");
        start.Environment.Remove("BASIN_HANDOFF");
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var lines = new List<string>();
        while (process.StandardOutput.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        Assert.True(process.WaitForExit(30_000), "dam did not exit");
        Assert.True(process.ExitCode == 0, $"dam exited {process.ExitCode}:\n{string.Join('\n', lines)}\n{stderr.Result}");
        return lines;
    }

    [Fact]
    public void A_retaining_exit_leaves_a_frame_the_next_start_inherits_without_a_modeset()
    {
        SkipUnlessRunnable();
        using var holder = new Holder();
        var first = RunDam("--handoff", "retain");
        Assert.Contains(first, line => line.StartsWith("HANDOFF retain Virtual-1 fb=", StringComparison.Ordinal));

        var second = RunDam();
        var inherit = Assert.Single(second, line => line.StartsWith("HANDOFF inherit", StringComparison.Ordinal));
        Assert.Matches(@"^HANDOFF inherit Virtual-1 mode=\d+x\d+@[\d.]+ modeset=no reason=matched$", inherit);
        Assert.DoesNotContain(second, line => line.StartsWith("HANDOFF retain", StringComparison.Ordinal));
    }

    [Fact]
    public void Handoff_off_modesets_and_restores_as_before()
    {
        SkipUnlessRunnable();
        using var holder = new Holder();
        _ = RunDam("--handoff", "retain");
        var off = RunDam("--handoff", "off");
        Assert.Contains(off, line => line.StartsWith("HANDOFF inherit Virtual-1", StringComparison.Ordinal) && line.EndsWith("modeset=yes reason=off", StringComparison.Ordinal));
        Assert.DoesNotContain(off, line => line.StartsWith("HANDOFF retain", StringComparison.Ordinal));

        var after = RunDam("--handoff", "off");
        Assert.DoesNotContain(after, line => line.StartsWith("HANDOFF retain", StringComparison.Ordinal));
    }
}
