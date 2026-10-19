using Xunit;

namespace TinyComp.Tests;

public sealed class GlassFrameTests
{
    private const string Config = "[frame]\nstyle = \"glass\"\n\n[frame.glass]\ncorner_radius = 12\n";

    public static TheoryData<string> Rows => ["gl", "vulkan"];

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_decorated_window_wears_the_glass_frame(string row)
    {
        using var session = QuillFrameTests.QuillSession.Start(row, Config);
        Assert.SkipWhen(session is null, $"tinycomp, ssdwin or the {row} row is not available beside the tests");
        Assert.False(session!.Logged("style = \"glass\""), $"the {row} row fell back from the glass frames: {session.Errors()}");

        var shot = session.Shot("glass");
        var (x, y) = session.WindowPosition();
        var titleY = y - 20;
        Assert.True(titleY >= 0, $"the frame's titlebar is off the top at y={y}");
        var background = Pixel(shot, 5, 5);
        var glass = Pixel(shot, x + 60, titleY);
        Assert.NotEqual(background, glass);

        var rim = Luma(Pixel(shot, x + 60, y - 33));
        var face = Luma(glass);
        Assert.True(rim > face + 8, $"the bevel facing the light is not brighter than the face: rim {rim}, face {face}");

        var body = Pixel(shot, x + 40, y + 40);
        Assert.NotEqual(body, Pixel(shot, x, y));
        Assert.Equal(body, Pixel(shot, x + 6, y + 6));
    }

    private static int Luma(uint rgb) => (int)(((rgb >> 16) & 0xFF) + ((rgb >> 8) & 0xFF) + (rgb & 0xFF)) / 3;

    [Fact]
    public void A_row_with_no_quill_host_falls_back_and_says_why()
    {
        using var session = QuillFrameTests.QuillSession.Start("pixman", Config);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        Assert.True(session!.Logged("style = \"glass\" cannot run"), $"pixman fell back without naming why: {session.Errors()}");
        var shot = session.Shot("fallback");
        var (x, y) = session.WindowPosition();
        Assert.NotEqual(Pixel(shot, 5, 5), Pixel(shot, x + 60, y - 20));
    }

    private static uint Pixel(QuillFrameTests.Shot shot, int x, int y)
    {
        var i = ((y * shot.Width) + x) * 4;
        return (uint)((shot.Rgba[i] << 16) | (shot.Rgba[i + 1] << 8) | shot.Rgba[i + 2]);
    }
}
