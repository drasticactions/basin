using Basin.Capabilities;
using Basin.Desktop;
using Basin.Sdl;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlHdrTests
{
    [Fact]
    public void The_published_description_follows_what_the_lease_presents()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Vulkan);
        var window = harness.Comp.Window;
        var color = harness.Host.Services.Require<ColorManager>();
        var global = harness.Host.Screens.GlobalFor(Basin.Shell.Nested.NestedShell.OutputKey)!;
        Assert.True(color.TryGetOutputDescription(global, out var before));
        Assert.NotEqual(ColorTransferFunction.St2084Pq, before.TransferNamed);
        Assert.False(window.Hdr.Enabled);

        window.ApplyHdr(new SdlHdrState(true, 240f, 4f));
        harness.Pump();
        Assert.True(color.TryGetOutputDescription(global, out var after));
        if (window.Hdr.Enabled)
        {
            Assert.Equal(ColorTransferFunction.St2084Pq, after.TransferNamed);
            Assert.Equal((0u, 960u, 240u), after.Luminances);
        }
        else
        {
            Assert.NotEqual(ColorTransferFunction.St2084Pq, after.TransferNamed);
        }

        window.ApplyHdr(SdlHdrState.Sdr);
        harness.Pump();
        Assert.False(window.Hdr.Enabled);
        Assert.True(color.TryGetOutputDescription(global, out var back));
        Assert.NotEqual(ColorTransferFunction.St2084Pq, back.TransferNamed);
    }

    [Fact]
    public void A_lease_that_switches_in_Begin_is_described_by_what_it_presents()
    {
        using var rig = new FakeLeaseRig();
        var color = rig.Host.Color!.Configuration;
        var output = rig.Window.View.Output;
        rig.Tick();
        Assert.NotEqual(ColorTransferFunction.St2084Pq, color.DescriptionOf(output).TransferNamed);

        var changes = 0;
        rig.Window.HdrChanged += _ => changes++;
        rig.Window.ApplyHdr(new SdlHdrState(true, 240f, 4f));
        Assert.False(rig.Window.Hdr.Enabled);
        rig.Tick();
        Assert.True(rig.Lease.Hdr.Enabled);
        Assert.Equal(ColorTransferFunction.St2084Pq, color.DescriptionOf(output).TransferNamed);
        Assert.Equal(1, changes);

        rig.Window.ApplyHdr(SdlHdrState.Sdr);
        rig.Tick();
        Assert.False(rig.Lease.Hdr.Enabled);
        Assert.NotEqual(ColorTransferFunction.St2084Pq, color.DescriptionOf(output).TransferNamed);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void The_GL_and_raster_leases_stay_SDR_without_a_color_manager()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Gl);
        Assert.Null(harness.Host.Color);
        harness.Comp.Window.ApplyHdr(new SdlHdrState(true, 240f, 4f));
        Assert.False(harness.Comp.Window.Hdr.Enabled);
    }
}
