using Basin.Capabilities;
using Basin.Color;
using Xunit;

namespace Basin.Tests;

public sealed class ScrgbTests
{
    private static readonly ImageDescription PqOutput = new()
    {
        PrimariesNamed = ColorPrimaries.Bt2020,
        TransferNamed = ColorTransferFunction.St2084Pq,
        Luminances = (0, 10000, 203),
    };

    [Fact]
    public void Windows_scrgb_decodes_one_as_eighty_nits_and_keeps_values_up_to_125()
    {
        var scrgb = TransferCharacteristics.From(ImageDescription.WindowsScrgb());
        Assert.Equal(80, scrgb.Decode(1), 6);
        Assert.Equal(320, scrgb.Decode(4), 6);
        Assert.Equal(10000, scrgb.Decode(125), 6);
        Assert.Equal(10000, scrgb.Decode(300), 6);
        Assert.Equal(0, scrgb.Decode(-1), 6);
        Assert.Equal(10000, scrgb.PeakLuminance, 6);
    }

    [Fact]
    public void An_scrgb_four_lands_at_the_pq_code_of_320_nits_through_the_shaper_and_cube()
    {
        var lut = ColorLutBaker.Bake(ImageDescription.WindowsScrgb(), PqOutput);
        Assert.Equal(80f, lut.ShaperNits);
        Assert.Equal(125f, lut.ShaperInputMax);
        var expected = (float)TransferCharacteristics.PqInverseEotf(320);
        var (r, g, b) = lut.SampleShaped(4f, 4f, 4f);
        Assert.Equal(expected, r, 3);
        Assert.Equal(expected, g, 3);
        Assert.Equal(expected, b, 3);
        Assert.True(r > TransferCharacteristics.PqInverseEotf(80) + 0.05, "a value above 1 is not clamped to 80 nits");
    }

    [Fact]
    public void An_sdr_source_bakes_without_a_shaper()
    {
        var lut = ColorLutBaker.Bake(ImageDescription.SdrDefault, PqOutput);
        Assert.Equal(0f, lut.ShaperNits);
        Assert.Equal(1f, lut.ShaperInputMax);
    }

    [Fact]
    public void Tetrahedral_sampling_matches_the_transform_on_the_grey_axis_and_stays_near_it_elsewhere()
    {
        var parameters = ColorTransformParameters.From(ImageDescription.SdrDefault, PqOutput);
        var lut = ColorLutBaker.Bake(ImageDescription.SdrDefault, PqOutput);
        Span<double> rgb = stackalloc double[3];
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var r = (float)random.NextDouble();
            var g = (float)random.NextDouble();
            var b = (float)random.NextDouble();
            (rgb[0], rgb[1], rgb[2]) = (r, g, b);
            parameters.Apply(rgb);
            var (tr, tg, tb) = lut.SampleTetrahedral(r, g, b);
            Assert.InRange(Math.Abs(tr - rgb[0]), 0, 4.0 / 1023);
            Assert.InRange(Math.Abs(tg - rgb[1]), 0, 4.0 / 1023);
            Assert.InRange(Math.Abs(tb - rgb[2]), 0, 4.0 / 1023);
        }
    }

    [Theory]
    [InlineData("vulkan")]
    [InlineData("gl")]
    public void An_fp16_scrgb_surface_above_one_reaches_the_pq_output_above_sdr_white(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        var content = new MemoryBuffer(8, 8, DrmFormat.Abgr16161616f);
        Assert.True(content.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            var four = BitConverter.HalfToUInt16Bits((Half)4f);
            var one = BitConverter.HalfToUInt16Bits((Half)1f);
            for (var y = 0; y < 8; y++)
            {
                var row = (ushort*)(view.Data + y * view.Stride);
                for (var x = 0; x < 8; x++)
                {
                    row[x * 4] = four;
                    row[(x * 4) + 1] = four;
                    row[(x * 4) + 2] = four;
                    row[(x * 4) + 3] = one;
                }
            }
        }

        content.EndDataAccess();
        var texture = host.Renderer.ImportTexture(content);
        Assert.SkipWhen(texture is null, $"{renderer} does not import an FP16 shm buffer");
        var target = new MemoryBuffer(32, 16, DrmFormat.Argb8888);
        var lut = host.Renderer.ImportLut(ColorLutBaker.Bake(ImageDescription.WindowsScrgb(), PqOutput));
        var pass = host.Renderer.BeginBufferPass(target, new RenderPassOptions { ColorDescription = PqOutput });
        pass.AddTexture(texture!, new TextureRenderOptions { DstBox = new Box(0, 0, 8, 8), ColorDescription = ImageDescription.WindowsScrgb() });
        pass.AddTexture(texture!, new TextureRenderOptions { DstBox = new Box(16, 0, 8, 8), Lut = lut });
        Assert.True(pass.Submit());

        var expected = (int)Math.Round(TransferCharacteristics.PqInverseEotf(320) * 255);
        var sdrWhite = (int)Math.Round(TransferCharacteristics.PqInverseEotf(80) * 255);
        Assert.True(target.BeginDataAccess(BufferDataAccess.Read, out var read));
        try
        {
            unsafe
            {
                var decomposed = *(uint*)(read.Data + (4 * read.Stride) + (4 * 4));
                var looked = *(uint*)(read.Data + (4 * read.Stride) + (20 * 4));
                Assert.InRange((int)((decomposed >> 8) & 0xFF), expected - 2, expected + 2);
                Assert.InRange((int)((looked >> 8) & 0xFF), expected - 2, expected + 2);
                Assert.True(((decomposed >> 8) & 0xFF) > sdrWhite + 10);
            }
        }
        finally
        {
            target.EndDataAccess();
        }

        lut?.Dispose();
        texture!.Dispose();
        target.Destroy();
        content.Destroy();
    }
}
