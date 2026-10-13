using Xunit;

namespace BasinGreeter.Tests;

public sealed class AllocationTests
{
    private static async Task<(long Frames, long Bytes, long Milliseconds)> Measure(bool repaint)
    {
        using var fixture = new Fixture();
        var extra = repaint ? new[] { "--frames", "1000000" } : [];
        using var greeter = fixture.Start("pixman", extra);
        await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        await greeter.SendAsync("clock 12:00");
        await Task.Delay(2000, TestContext.Current.CancellationToken);
        var mark = greeter.Mark();
        await greeter.SendAsync("alloc-mark");
        await greeter.ExpectAsync(line => line == "ALLOCMARK", "the mark", since: mark);
        await Task.Delay(3000, TestContext.Current.CancellationToken);
        mark = greeter.Mark();
        await greeter.SendAsync("alloc-since");
        var line = await greeter.ExpectAsync(line => line.StartsWith("ALLOCSINCE ", StringComparison.Ordinal), "the measurement", since: mark);
        var fields = line.Split(' ').Select(field => field.Split('=')).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => long.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture));
        return (fields["frames"], fields["bytes"], Math.Max(1, fields["ms"]));
    }

    [Fact]
    public async Task Repainting_the_idle_greeter_allocates_nothing_per_frame()
    {
        var idle = await Measure(repaint: false);
        var repainting = await Measure(repaint: true);
        Assert.True(idle.Frames <= 5, $"the idle greeter drew {idle.Frames} frames");
        Assert.True(repainting.Frames >= 120, $"only {repainting.Frames} frames were drawn");
        var idleOverSameTime = (double)idle.Bytes * repainting.Milliseconds / idle.Milliseconds;
        var perFrame = (repainting.Bytes - idleOverSameTime) / repainting.Frames;
        Assert.True(perFrame < 8, $"{repainting.Bytes} bytes over {repainting.Frames} frames in {repainting.Milliseconds} ms against {idle.Bytes} bytes idle in {idle.Milliseconds} ms: {perFrame:F1} bytes a frame");
    }
}
