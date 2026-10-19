using Basin.Sdl;
using SkiaSharp;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlCompProcessTests
{
    public static TheoryData<string> Leases => new(SdlRenderDevices.Names);

    [Theory]
    [MemberData(nameof(Leases))]
    public void Weston_simple_shm_shows(string lease)
    {
        var client = SdlCompProcess.Which("weston-simple-shm");
        Assert.SkipWhen(client is null, "weston-simple-shm is not installed");
        using var comp = new SdlCompProcess("--renderer", lease, "--width", "640", "--height", "400");
        comp.StartClient(client!);
        Thread.Sleep(1500);
        AssertPainted(comp);
    }

    [Fact]
    public void A_waypipe_client_over_a_unix_socket_shows()
    {
        Assert.SkipWhen(SdlCompProcess.Which("waypipe") is null, "waypipe is not installed");
        Assert.SkipWhen(SdlCompProcess.Which("weston-simple-shm") is null, "weston-simple-shm is not installed");
        var socket = Path.Combine("/tmp", $"sdl-wp-{Guid.NewGuid():N}"[..20] + ".sock");
        using var comp = new SdlCompProcess("--renderer", SdlRenderDevices.Raster, "--width", "640", "--height", "400", "--waypipe-listen", socket);
        comp.WaitFor("LISTEN ", TimeSpan.FromSeconds(10));
        comp.StartWaypipe("-s", socket, "--no-gpu", "--oneshot", "server", "--", "weston-simple-shm");
        comp.WaitFor("CHANNELS 1", TimeSpan.FromSeconds(15));
        Thread.Sleep(1000);
        AssertPainted(comp);
    }

    [Fact]
    public void A_waypipe_client_over_vsock_shows()
    {
        Assert.SkipWhen(Basin.Tests.VsockFacts.WaypipeReason() is { } reason, Basin.Tests.VsockFacts.WaypipeReason() ?? string.Empty);
        Assert.SkipWhen(SdlCompProcess.Which("weston-simple-shm") is null, "weston-simple-shm is not installed");
        var port = (uint)Random.Shared.Next(20_000, 60_000);
        using var comp = new SdlCompProcess("--renderer", SdlRenderDevices.Raster, "--width", "640", "--height", "400", "--waypipe-listen", $"vsock:{port}");
        comp.WaitFor("LISTEN ", TimeSpan.FromSeconds(10));
        comp.StartWaypipe("--vsock", "-s", $"1:{port}", "--compress", "lz4", "--no-gpu", "--oneshot", "server", "--", "weston-simple-shm");
        comp.WaitFor("CHANNELS 1", TimeSpan.FromSeconds(15));
        Thread.Sleep(1000);
        AssertPainted(comp);
    }

    private static void AssertPainted(SdlCompProcess comp)
    {
        var shot = Path.Combine(comp.RuntimeDir, "shot.png");
        comp.Send($"shot {shot}");
        comp.WaitFor("SHOT ", TimeSpan.FromSeconds(10));
        using var bitmap = SKBitmap.Decode(shot);
        Assert.Equal(640, bitmap.Width);
        var background = bitmap.GetPixel(600, 380);
        var differing = 0;
        for (var y = 0; y < 250; y += 5)
        {
            for (var x = 0; x < 250; x += 5)
            {
                if (bitmap.GetPixel(x, y) != background)
                {
                    differing++;
                }
            }
        }

        Assert.True(differing > 1000, $"only {differing} sampled pixels differ from the background");
    }
}
