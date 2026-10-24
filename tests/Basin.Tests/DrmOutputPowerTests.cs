using System.Runtime.CompilerServices;
using Basin.Backend.Drm;
using Basin.Backend.Headless;
using Xunit;

namespace Basin.Tests;

public sealed class DrmOutputPowerTests
{
    [Fact]
    public void An_output_enabled_after_a_power_off_reads_on()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = backend.CreateOutput(new OutputMode(160, 120, 60_000));
        var power = new DrmOutputPower();
        var changes = 0;
        power.PowerChanged += _ => changes++;

        Assert.True(power.SetOn(output, false));
        Assert.False(power.IsOn(output));
        Assert.Equal(1, changes);

        using (var state = new OutputState())
        {
            Assert.True(output.Commit(state.SetEnabled(true)));
        }

        Assert.True(power.IsOn(output));
        output.Destroy();
    }

    [Fact]
    public void A_destroyed_output_is_not_kept_alive()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var power = new DrmOutputPower();
        var weak = TurnOffAndDestroy(backend, power);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.TryGetTarget(out _));
        GC.KeepAlive(power);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<HeadlessOutput> TurnOffAndDestroy(HeadlessBackend backend, DrmOutputPower power)
    {
        var output = backend.CreateOutput(new OutputMode(160, 120, 60_000));
        Assert.True(power.SetOn(output, false));
        output.Destroy();
        return new WeakReference<HeadlessOutput>(output);
    }
}
