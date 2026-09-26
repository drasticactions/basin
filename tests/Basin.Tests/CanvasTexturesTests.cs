using Basin.Effects;
using Xunit;

namespace Basin.Tests;

public sealed class CanvasTexturesTests
{
    public static TheoryData<CanvasTexturePreset> Presets =>
        [CanvasTexturePreset.Stone, CanvasTexturePreset.Brick, CanvasTexturePreset.Wood, CanvasTexturePreset.Noise];

    private static unsafe uint[] Pixels(MemoryBuffer buffer)
    {
        var pixels = new uint[buffer.Width * buffer.Height];
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            for (var y = 0; y < buffer.Height; y++)
            {
                new ReadOnlySpan<uint>((void*)(view.Data + (y * view.Stride)), buffer.Width).CopyTo(pixels.AsSpan(y * buffer.Width));
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }

        return pixels;
    }

    private static uint[] Generate(CanvasTexturePreset preset)
    {
        var buffer = CanvasTextures.Generate(preset);
        try
        {
            Assert.Equal(CanvasTextures.Size, buffer.Width);
            Assert.Equal(CanvasTextures.Size, buffer.Height);
            Assert.Equal(DrmFormat.Argb8888, buffer.Format);
            return Pixels(buffer);
        }
        finally
        {
            buffer.Destroy();
        }
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void A_preset_is_opaque_grayscale_and_the_same_every_time(CanvasTexturePreset preset)
    {
        var first = Generate(preset);
        foreach (var pixel in first)
        {
            Assert.Equal(0xffu, pixel >> 24);
            var r = (pixel >> 16) & 0xff;
            Assert.Equal(r, (pixel >> 8) & 0xff);
            Assert.Equal(r, pixel & 0xff);
        }

        Assert.Equal(first, Generate(preset));
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void A_preset_tiles_with_no_seam_on_either_axis(CanvasTexturePreset preset)
    {
        const int size = CanvasTextures.Size;
        var pixels = Generate(preset);
        double Row(int a, int b)
        {
            var sum = 0.0;
            for (var x = 0; x < size; x++)
            {
                sum += Math.Abs((int)(pixels[(a * size) + x] & 0xff) - (int)(pixels[(b * size) + x] & 0xff));
            }

            return sum / size;
        }

        double Column(int a, int b)
        {
            var sum = 0.0;
            for (var y = 0; y < size; y++)
            {
                sum += Math.Abs((int)(pixels[(y * size) + a] & 0xff) - (int)(pixels[(y * size) + b] & 0xff));
            }

            return sum / size;
        }

        var rowStep = 0.0;
        var columnStep = 0.0;
        for (var i = 0; i < size - 1; i++)
        {
            rowStep = Math.Max(rowStep, Row(i, i + 1));
            columnStep = Math.Max(columnStep, Column(i, i + 1));
        }

        Assert.True(Row(size - 1, 0) <= rowStep, $"row seam {Row(size - 1, 0)} > {rowStep}");
        Assert.True(Column(size - 1, 0) <= columnStep, $"column seam {Column(size - 1, 0)} > {columnStep}");
    }

    [Fact]
    public void The_noise_preset_is_smooth_across_the_wrap()
    {
        const int size = CanvasTextures.Size;
        var pixels = Generate(CanvasTexturePreset.Noise);
        var worst = 0;
        for (var i = 0; i < size; i++)
        {
            worst = Math.Max(worst, Math.Abs((int)(pixels[i] & 0xff) - (int)(pixels[((size - 1) * size) + i] & 0xff)));
            worst = Math.Max(worst, Math.Abs((int)(pixels[i * size] & 0xff) - (int)(pixels[(i * size) + size - 1] & 0xff)));
        }

        Assert.True(worst <= 12, $"wrap step {worst}");
    }

    [Fact]
    public void The_names_parse_and_anything_else_does_not()
    {
        Assert.True(CanvasTextures.TryParse("stone", out var stone));
        Assert.Equal(CanvasTexturePreset.Stone, stone);
        Assert.True(CanvasTextures.TryParse("noise", out var noise));
        Assert.Equal(CanvasTexturePreset.Noise, noise);
        Assert.False(CanvasTextures.TryParse("stnoe", out _));
        Assert.False(CanvasTextures.TryParse("Stone", out _));
        Assert.False(CanvasTextures.TryParse("none", out _));
        Assert.Equal("wood", CanvasTextures.NameOf(CanvasTexturePreset.Wood));
    }
}
