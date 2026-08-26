using Basin.Diagnostics;
using Basin.Tests;
using Waylonia.Audio;
using Xunit;

namespace Waylonia.Tests;

public sealed class AudioRingTests : IDisposable
{
    public AudioRingTests() => AllocationScope.Reset();

    public void Dispose() => AllocationScope.Reset();

    private static float[] Ramp(int count, int from = 0)
    {
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = from + i;
        }

        return samples;
    }

    [Fact]
    public void A_write_reads_back_in_order()
    {
        var ring = new AudioRing(2, 64, 0);
        var written = Ramp(8);

        Assert.Equal(8, ring.Write(written));
        Assert.Equal(8, ring.Depth);

        var read = new float[8];
        Assert.Equal(8, ring.Read(read));
        Assert.Equal(written, read);
        Assert.Equal(0, ring.Depth);
    }

    [Fact]
    public void A_write_that_wraps_the_capacity_reads_back_contiguous()
    {
        var ring = new AudioRing(2, 8, 4);
        Assert.Equal(16, ring.Capacity);

        var drain = new float[12];
        ring.Write(Ramp(12));
        ring.Read(drain);

        var straddling = Ramp(12, from: 100);
        Assert.Equal(12, ring.Write(straddling));

        var read = new float[12];
        Assert.Equal(12, ring.Read(read));
        Assert.Equal(straddling, read);
    }

    [Fact]
    public void An_underrun_delivers_what_it_has_and_silence_after_it()
    {
        var ring = new AudioRing(2, 64, 0);
        ring.Write(Ramp(4, from: 1));

        var read = new float[8];
        Assert.Equal(4, ring.Read(read));
        Assert.Equal([1f, 2f, 3f, 4f, 0f, 0f, 0f, 0f], read);
        Assert.Equal(1, ring.Underruns);
    }

    [Fact]
    public void An_overrun_drops_the_oldest_and_settles_at_the_target_depth()
    {
        var ring = new AudioRing(2, 16, 4);
        Assert.Equal(32, ring.Capacity);
        Assert.Equal(8, ring.Target);

        for (var block = 0; block < 8; block++)
        {
            ring.Write(Ramp(16, from: block * 16));
        }

        var read = new float[8];
        Assert.Equal(8, ring.Read(read));
        Assert.Equal(Ramp(8, from: 120), read);
        Assert.Equal(0, ring.Depth);
        Assert.True(ring.Dropped > 0);
    }

    [Fact]
    public void Nothing_plays_until_the_ring_reaches_the_target_depth()
    {
        var ring = new AudioRing(2, 64, 8);
        Assert.Equal(16, ring.Target);
        Assert.False(ring.Primed);

        ring.Write(Ramp(8, from: 1));
        var read = new float[8];
        Assert.Equal(0, ring.Read(read));
        Assert.Equal(new float[8], read);
        Assert.Equal(0, ring.Underruns);
        Assert.False(ring.Primed);
        Assert.Equal(8, ring.Depth);

        ring.Write(Ramp(8, from: 9));
        Assert.Equal(8, ring.Read(read));
        Assert.True(ring.Primed);
        Assert.Equal(Ramp(8, from: 1), read);
    }

    [Fact]
    public void A_read_of_nothing_and_a_write_of_nothing_are_both_no_ops()
    {
        var ring = new AudioRing(2, 64, 32);

        Assert.Equal(0, ring.Write([]));
        Assert.Equal(0, ring.Read([]));
        Assert.Equal(0, ring.Depth);
        Assert.Equal(0, ring.Underruns);

        ring.Write(Ramp(4));
        Assert.Equal(0, ring.Read([]));
        Assert.Equal(4, ring.Depth);
    }

    [Fact]
    public void A_burst_larger_than_the_ring_keeps_its_newest_samples()
    {
        var ring = new AudioRing(2, 8, 4);
        Assert.Equal(16, ring.Capacity);

        Assert.Equal(16, ring.Write(Ramp(40)));

        var read = new float[8];
        Assert.Equal(8, ring.Read(read));
        Assert.Equal(Ramp(8, from: 24), read);
    }

    [Fact]
    public void Neither_side_allocates()
    {
        LeakTracking.Require();

        var ring = AudioRing.ForSession(48000, 2);
        var block = new float[960];
        var read = new float[480];
        for (var i = 0; i < 16; i++)
        {
            ring.Write(block);
        }

        ring.Read(read);
        Assert.True(ring.Primed);

        AllocationScope.Begin();
        for (var i = 0; i < 10_000; i++)
        {
            ring.Write(block);
            ring.Read(read);
            ring.Read(read);
        }

        AllocationScope.End();
    }
}
