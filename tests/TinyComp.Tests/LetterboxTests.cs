using Xunit;

namespace TinyComp.Tests;

public sealed class LetterboxTests
{
    private const string Config = "[frame]\nstyle = \"flat\"\n\n[overview]\nhot_corner = \"none\"\n";

    private static uint At(QuillFrameTests.Shot shot, int x, int y)
    {
        var i = ((y * shot.Width) + x) * 4;
        return (uint)((shot.Rgba[i] << 16) | (shot.Rgba[i + 1] << 8) | shot.Rgba[i + 2]);
    }

    [Fact]
    public void A_fullscreen_window_that_keeps_its_size_is_fitted_with_black_bars()
    {
        using var session = QuillFrameTests.QuillSession.Start("pixman", Config, ["server", "fullscreen", "pixel"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");

        var shot = session!.Shot("letterbox");
        var centerY = shot.Height / 2;
        Assert.Equal(0x3C7A9Eu, At(shot, (shot.Width / 2) + 100, centerY + 100));
        Assert.Equal(0x3C7A9Eu, At(shot, (shot.Width / 2) - (shot.Height / 2) + 4, centerY));
        Assert.Equal(0x000000u, At(shot, 4, centerY));
        Assert.Equal(0x000000u, At(shot, shot.Width - 4, centerY));
        Assert.Equal(0x3C7A9Eu, At(shot, shot.Width / 2, 4));
    }

    [Fact]
    public void A_process_rule_with_letterbox_center_keeps_the_window_one_to_one()
    {
        var config = Config + "\n[[rule]]\nprocess = \"ssdwin\"\nletterbox = \"center\"\nscale_filter = \"nearest\"\n";
        using var session = QuillFrameTests.QuillSession.Start("pixman", config, ["server", "fullscreen", "pixel"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");

        var shot = session!.Shot("letterbox-center");
        var centerX = shot.Width / 2;
        var centerY = shot.Height / 2;
        Assert.Equal(0x3C7A9Eu, At(shot, centerX + 100, centerY + 100));
        Assert.Equal(0x3C7A9Eu, At(shot, centerX - 127, centerY - 127));
        Assert.Equal(0x000000u, At(shot, centerX - 129, centerY));
        Assert.Equal(0x000000u, At(shot, centerX, centerY - 129));
        Assert.Equal(0x000000u, At(shot, centerX + 129, centerY));
    }
}
