using Basin.Sdl;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlLeaseTests
{
    public static TheoryData<string> Leases => new(SdlRenderDevices.Names);

    private static readonly Action<nint, int> Quadrants = (data, stride) =>
    {
        unsafe
        {
            for (var y = 0; y < 60; y++)
            {
                var row = (uint*)((byte*)data + (y * stride));
                for (var x = 0; x < 80; x++)
                {
                    row[x] = (x < 40, y < 30) switch
                    {
                        (true, true) => 0xFFFF0000u,
                        (false, true) => 0xFF00FF00u,
                        (true, false) => 0xFF0000FFu,
                        _ => 0xFFFFFFFFu,
                    };
                }
            }
        }
    };

    [Theory]
    [MemberData(nameof(Leases))]
    public void A_toplevel_matches_its_golden_at_scale_1(string lease) => Golden(lease, 1.0);

    [Theory]
    [MemberData(nameof(Leases))]
    public void A_toplevel_matches_its_golden_at_scale_2(string lease) => Golden(lease, 2.0);

    private static void Golden(string lease, double scale)
    {
        using var harness = new SdlCompHarness(lease, scale);
        harness.MapToplevel(80, 60, Quadrants);
        var pixels = harness.Capture();
        var target = harness.Comp.Window.View.Target;
        Assert.Equal(scale, target.Scale);
        var window = Assert.Single(harness.Comp.Shell.Windows);
        var x = (int)Math.Round(window.ClientBox.X * scale);
        var y = (int)Math.Round(window.ClientBox.Y * scale);
        var redX = x + (int)(10 * scale);
        var redY = y + (int)(10 * scale);
        var whiteX = x + (int)(70 * scale);
        var whiteY = y + (int)(50 * scale);
        Assert.Equal((255, 0, 0), Pixel(pixels, target.Width, redX, redY));
        Assert.Equal((255, 255, 255), Pixel(pixels, target.Width, whiteX, whiteY));
        Tests.Golden.AssertMatches(pixels, target.Width, target.Height, $"toplevel-{lease}-x{scale:0}", lease == SdlRenderDevices.Raster ? 0 : 2);
    }

    private static (int R, int G, int B) Pixel(byte[] rgba, int width, int x, int y)
    {
        var i = ((y * width) + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2]);
    }

    [Theory]
    [MemberData(nameof(Leases))]
    public void A_thousand_frames_allocate_nothing(string lease)
    {
        using var harness = new SdlCompHarness(lease);
        harness.MapToplevel(80, 60, Quadrants);
        var loop = harness.Comp.FrameLoop;
        var window = harness.Comp.Window;
        var id = window.Window.Id;
        for (var i = 0; i < 50; i++)
        {
            SdlEvents.Motion(id, 10 + (i % 50), 20);
            window.View.RequestRender?.Invoke();
            loop.RunOnce(0);
        }

        var presented = window.Presented;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            SdlEvents.Motion(id, 10 + (i % 50), 20);
            window.View.RequestRender?.Invoke();
            loop.RunOnce(0);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1000, window.Presented - presented);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void A_frame_that_composites_nothing_is_discarded()
    {
        using var rig = new FakeLeaseRig();
        rig.Tick();
        var output = rig.Window.View.SceneOutput;
        var presents = rig.Lease.Presents;
        Action<Basin.FrameTick> empties = _ => output.Ring.Commit();
        output.BeforeRepaint += empties;
        for (var i = 0; i < 10; i++)
        {
            output.Ring.Add(new Basin.Box(0, 0, 8, 8));
            Assert.True(rig.Window.NeedsFrame);
            rig.Tick();
        }

        output.BeforeRepaint -= empties;
        Assert.Equal(10, rig.Lease.Discards);

        Action<Basin.FrameTick> throws = _ => throw new InvalidOperationException("a failed commit");
        output.BeforeRepaint += throws;
        for (var i = 0; i < 10; i++)
        {
            output.Ring.Add(new Basin.Box(0, 0, 8, 8));
            rig.Tick();
        }

        output.BeforeRepaint -= throws;
        Assert.Equal(20, rig.Lease.Discards);
        Assert.Equal(presents, rig.Lease.Presents);
        Assert.Equal(rig.Lease.Begins, rig.Lease.Presents + rig.Lease.Discards);
    }

    [Fact]
    public void The_Vulkan_lease_presents_after_many_discarded_frames()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Vulkan);
        var window = harness.Comp.Window;
        var output = window.View.SceneOutput;
        harness.Pump();
        var presented = window.Presented;
        Action<Basin.FrameTick> empties = _ => output.Ring.Commit();
        output.BeforeRepaint += empties;
        for (var i = 0; i < 20; i++)
        {
            output.Ring.Add(new Basin.Box(0, 0, 8, 8));
            harness.Comp.Loop.MarkActivity();
            harness.Step(0);
        }

        output.BeforeRepaint -= empties;
        Assert.Equal(presented, window.Presented);
        window.View.RequestRender?.Invoke();
        harness.PumpUntil(() => window.Presented > presented, "the Vulkan lease never presented after the discarded frames");
    }
}
