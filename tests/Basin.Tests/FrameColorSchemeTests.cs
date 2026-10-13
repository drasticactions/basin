using Basin.Capabilities;
using Basin.UI.Skia;
using Xunit;

namespace Basin.Tests;

public sealed class FrameColorSchemeTests
{
    private static uint TitlePixel(FrameColorScheme scheme)
    {
        using var theme = FlatFrameTheme.Default();
        using var host = new SkiaUIHost();
        var renderer = new FlatFrameRenderer(theme);
        var state = new FrameState { Title = "t", Active = true, ColorScheme = scheme };
        var insets = renderer.Measure(state, 1.0);
        var surface = host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 160 + insets.Left + insets.Right,
            Height = 60 + insets.Top + insets.Bottom,
            Scale = 1.0,
        })!;
        renderer.Draw(surface, new Box(insets.Left, insets.Top, 160, 60), state, default);
        Assert.True(surface.TryAcquire(out var frame));
        try
        {
            var buffer = (MemoryBuffer)frame.Buffer!;
            Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var access));
            try
            {
                unsafe
                {
                    return ((uint*)(access.Data + (insets.Top / 2 * access.Stride)))[insets.Left + 40] & 0x00FFFFFFu;
                }
            }
            finally
            {
                buffer.EndDataAccess();
            }
        }
        finally
        {
            frame.Dispose();
            surface.Dispose();
        }
    }

    [Fact]
    public void The_flat_frame_follows_a_light_or_dark_scheme()
    {
        var standard = TitlePixel(FrameColorScheme.Default);
        Assert.Equal(standard, TitlePixel(FrameColorScheme.Dark));
        Assert.Equal(0xECEDF0u, TitlePixel(FrameColorScheme.Light));
        Assert.Equal(0x23252Bu, standard);
    }
}
