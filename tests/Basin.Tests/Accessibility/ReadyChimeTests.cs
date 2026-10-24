using Basin.Audio.PipeWire;
using Xunit;

namespace Basin.Tests;

public sealed class ReadyChimeTests
{
    [Fact]
    public void The_chime_is_short_quiet_and_starts_and_ends_in_silence()
    {
        var clip = ReadyChime.Create();
        Assert.InRange(clip.Duration.TotalSeconds, 0.29, 0.31);
        Assert.InRange(clip.Samples.Max(Math.Abs), 0.2f, 0.26f);
        Assert.Equal(0f, clip.Samples[0]);
        Assert.InRange(Math.Abs(clip.Samples[^1]), 0f, 0.001f);
    }
}
