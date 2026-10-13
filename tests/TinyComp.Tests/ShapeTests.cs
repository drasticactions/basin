using Basin.Tests;
using Xunit;

namespace TinyComp.Tests;

public sealed class ShapeTests
{
    [Theory]
    [InlineData("pixman")]
    [InlineData("vulkan")]
    public void A_shaped_window_shows_the_desktop_outside_its_shape(string renderer)
    {
        var config = $"[frame]\nstyle = \"none\"\n\n[overview]\nhot_corner = \"none\"\n\n[compositor]\nbackground = \"#30a050\"\nrenderer = \"{renderer}\"\n";
        using var session = EmulationTests.Session.Start("0x0", 0, config, "exec xeyes -geometry 300x200");
        Assert.SkipWhen(session is null || !OnPath("xeyes"), "tinycomp, Xwayland or xeyes is not available");
        Assert.SkipWhen(renderer != "pixman" && !RendererRows.IsRunnable(renderer), $"{renderer} cannot run here");

        string? where = null;
        for (var i = 0; i < 40 && where is null; i++)
        {
            session!.Send("where");
            where = session.WaitForLine("XWIN XEyes", 500);
        }

        Assert.NotNull(where);
        var parts = where!.Split(' ');
        var x = int.Parse(parts[2]);
        var y = int.Parse(parts[3]);
        Thread.Sleep(800);
        var shot = session!.Shot($"eyes-{renderer}");
        Assert.Equal(0x30A050u, EmulationTests.At(shot, x + 2, y + 2));
        Assert.Equal(0x30A050u, EmulationTests.At(shot, x + 150, y + 3));
        Assert.Equal(0xFFFFFFu, EmulationTests.At(shot, x + 75, y + 60));
    }

    private static bool OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':').Any(dir => File.Exists(Path.Combine(dir, program)));
}
