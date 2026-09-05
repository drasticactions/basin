using Basin.Capabilities;
using Basin.Color;
using Xunit;

namespace Basin.Tests;

public sealed class KmsColorPipelineTests
{
    private static readonly ImageDescription WidePanel = OutputDescriptions.Sdr(
        (0.68, 0.32, 0.265, 0.69, 0.15, 0.06, 0.3127, 0.3290));

    [Fact]
    public void The_srgb_to_srgb_matrix_is_identity()
    {
        var matrix = KmsColorPipeline.GamutCtm(ImageDescription.SdrDefault, ImageDescription.SdrDefault);
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(i % 4 == 0 ? 1.0 : 0.0, matrix[i], 6);
        }
    }

    [Fact]
    public void The_gamut_matrix_maps_white_to_white()
    {
        var matrix = KmsColorPipeline.GamutCtm(ImageDescription.SdrDefault, WidePanel);
        var white = Colorimetry.Apply(matrix, [1.0, 1.0, 1.0]);
        Assert.Equal(1.0, white[0], 3);
        Assert.Equal(1.0, white[1], 3);
        Assert.Equal(1.0, white[2], 3);
        Assert.True(matrix[1] > 0 || matrix[2] > 0);
    }

    [Fact]
    public void Decode_ramps_are_monotonic_and_span_the_range()
    {
        var ramps = KmsColorPipeline.DecodeRamps(ImageDescription.SdrDefault, 256);
        Assert.Equal(256, ramps.Red.Length);
        Assert.Equal(0, ramps.Red[0]);
        Assert.Equal(ushort.MaxValue, ramps.Red[255]);
        for (var i = 1; i < 256; i++)
        {
            Assert.True(ramps.Red[i] >= ramps.Red[i - 1]);
        }

        Assert.True(ramps.Red[128] < 128 * 257);
    }

    [Fact]
    public void Encode_ramps_invert_the_decode_ramps()
    {
        var decode = KmsColorPipeline.DecodeRamps(ImageDescription.SdrDefault, 4096);
        var encode = KmsColorPipeline.EncodeRamps(ImageDescription.SdrDefault, 4096);
        for (var i = 255; i < 4096; i += 64)
        {
            var linear = decode.Red[i] / (double)ushort.MaxValue;
            var back = encode.Red[(int)Math.Round(linear * 4095)] / (double)ushort.MaxValue;
            Assert.Equal(i / 4095.0, back, 0.01);
        }
    }

    [Fact]
    public void Night_light_multipliers_scale_the_encode_ramps()
    {
        var multipliers = NightLight.Multipliers(3500);
        var plain = KmsColorPipeline.EncodeRamps(ImageDescription.SdrDefault, 256);
        var warm = KmsColorPipeline.EncodeRamps(ImageDescription.SdrDefault, 256, multipliers);
        Assert.InRange(Math.Abs(warm.Red[200] - plain.Red[200]), 0, 700);
        Assert.True(warm.Blue[200] < plain.Blue[200]);
    }

    private static double EvalRamp(ushort[] ramp, double value)
    {
        var scaled = Math.Clamp(value, 0, 1) * (ramp.Length - 1);
        var index = (int)scaled;
        var fraction = scaled - index;
        var low = ramp[index] / (double)ushort.MaxValue;
        var high = ramp[Math.Min(index + 1, ramp.Length - 1)] / (double)ushort.MaxValue;
        return low + ((high - low) * fraction);
    }

    private static (double R, double G, double B) EvalPipeline(
        in OutputGammaRamps decode, double[] ctm, in OutputGammaRamps encode, double r, double g, double b)
    {
        var linear = Colorimetry.Apply(
            ctm, [EvalRamp(decode.Red, r), EvalRamp(decode.Green, g), EvalRamp(decode.Blue, b)]);
        return (EvalRamp(encode.Red, linear[0]), EvalRamp(encode.Green, linear[1]), EvalRamp(encode.Blue, linear[2]));
    }

    private static Lcms2.IccProfile Gamma22SrgbProfile()
    {
        var white = new Lcms2.Native.cmsCIExyY { x = 0.3127, y = 0.3290, Y = 1 };
        var primaries = new Lcms2.Native.cmsCIExyYTRIPLE
        {
            Red = new Lcms2.Native.cmsCIExyY { x = 0.64, y = 0.33, Y = 1 },
            Green = new Lcms2.Native.cmsCIExyY { x = 0.30, y = 0.60, Y = 1 },
            Blue = new Lcms2.Native.cmsCIExyY { x = 0.15, y = 0.06, Y = 1 },
        };
        using var gamma = Lcms2.ToneCurve.BuildGamma(2.2);
        return Lcms2.IccProfile.CreateRgb(white, primaries, [gamma, gamma, gamma]);
    }

    private static byte[] MatrixShaperProfile(double redGamma, double greenGamma, double blueGamma)
    {
        var white = new Lcms2.Native.cmsCIExyY { x = 0.3127, y = 0.3290, Y = 1 };
        var primaries = new Lcms2.Native.cmsCIExyYTRIPLE
        {
            Red = new Lcms2.Native.cmsCIExyY { x = 0.68, y = 0.32, Y = 1 },
            Green = new Lcms2.Native.cmsCIExyY { x = 0.265, y = 0.69, Y = 1 },
            Blue = new Lcms2.Native.cmsCIExyY { x = 0.15, y = 0.06, Y = 1 },
        };
        using var red = Lcms2.ToneCurve.BuildGamma(redGamma);
        using var green = Lcms2.ToneCurve.BuildGamma(greenGamma);
        using var blue = Lcms2.ToneCurve.BuildGamma(blueGamma);
        using var profile = Lcms2.IccProfile.CreateRgb(white, primaries, [red, green, blue]);
        return profile.SaveToArray();
    }

    [Fact]
    public void A_matrix_shaper_profile_decomposes_to_the_lcms_transform()
    {
        Assert.SkipUnless(Lcms2Support.IsAvailable, "liblcms2 ≥ 2.19 not present");
        var icc = MatrixShaperProfile(2.4, 2.2, 2.0);
        Assert.True(KmsColorPipeline.TryExtractMatrixShaper(icc, 4096, out var ctm, out var encode));
        var decode = KmsColorPipeline.DecodeRamps(ImageDescription.SdrDefault, 4096);

        using var source = Gamma22SrgbProfile();
        using var device = Lcms2.IccProfile.FromMemory(icc);
        using var reference = Lcms2.ColorTransform.Create(
            source, Lcms2.PixelFormat.RgbFloat, device, Lcms2.PixelFormat.RgbFloat,
            Lcms2.RenderingIntent.RelativeColorimetric);

        const int steps = 8;
        var input = new float[steps * steps * steps * 3];
        var index = 0;
        for (var b = 0; b < steps; b++)
        {
            for (var g = 0; g < steps; g++)
            {
                for (var r = 0; r < steps; r++)
                {
                    input[index++] = r / (float)(steps - 1);
                    input[index++] = g / (float)(steps - 1);
                    input[index++] = b / (float)(steps - 1);
                }
            }
        }

        var expected = new float[input.Length];
        reference.Transform<float, float>(input, expected, steps * steps * steps);

        var worst = 0.0;
        var worstBright = 0.0;
        for (var i = 0; i < input.Length; i += 3)
        {
            var (r, g, b) = EvalPipeline(decode, ctm, encode, input[i], input[i + 1], input[i + 2]);
            var error = Math.Max(
                Math.Abs(r - Math.Clamp(expected[i], 0, 1)),
                Math.Max(
                    Math.Abs(g - Math.Clamp(expected[i + 1], 0, 1)),
                    Math.Abs(b - Math.Clamp(expected[i + 2], 0, 1))));
            worst = Math.Max(worst, error);
            if (input[i] >= 0.25f && input[i + 1] >= 0.25f && input[i + 2] >= 0.25f)
            {
                worstBright = Math.Max(worstBright, error);
            }
        }

        Assert.True(worst < 1.0 / 128, $"max channel error {worst} is over 1/128");
        Assert.True(worstBright < 1.0 / 256, $"max channel error away from black {worstBright} is over 1/256");
    }

    [Fact]
    public void A_profile_that_is_not_rgb_matrix_shaper_is_refused()
    {
        Assert.SkipUnless(Lcms2Support.IsAvailable, "liblcms2 ≥ 2.19 not present");
        Assert.False(KmsColorPipeline.TryExtractMatrixShaper([1, 2, 3], 256, out _, out _));

        using var lab = Lcms2.IccProfile.CreateLab4(null);
        Assert.False(KmsColorPipeline.TryExtractMatrixShaper(lab.SaveToArray(), 256, out _, out _));
    }

    [Fact]
    public void Extracted_gamma_ramps_are_per_channel()
    {
        Assert.SkipUnless(Lcms2Support.IsAvailable, "liblcms2 ≥ 2.19 not present");
        var icc = MatrixShaperProfile(2.4, 2.2, 2.0);
        Assert.True(KmsColorPipeline.TryExtractMatrixShaper(icc, 1024, out _, out var gamma));
        Assert.True(gamma.Red[512] > gamma.Green[512]);
        Assert.True(gamma.Green[512] > gamma.Blue[512]);
    }

    [Fact]
    public void Hdr_and_icc_descriptions_cannot_be_expressed()
    {
        Assert.True(KmsColorPipeline.CanExpress(ImageDescription.SdrDefault, WidePanel));
        Assert.False(KmsColorPipeline.CanExpress(ImageDescription.SdrDefault, OutputDescriptions.Hdr10(600, 0.05)));
        Assert.False(KmsColorPipeline.CanExpress(
            ImageDescription.SdrDefault, ImageDescription.SdrDefault with { IccData = [1, 2, 3] }));
    }

    [Fact]
    public void Edr_headroom_is_expressible_and_a_dimmer_panel_is_not()
    {
        Assert.True(KmsColorPipeline.CanExpress(
            ImageDescription.SdrDefault, WidePanel with { Luminances = (0, 160, 80) }));
        Assert.False(KmsColorPipeline.CanExpress(
            ImageDescription.SdrDefault, WidePanel with { Luminances = (0, 40, 80) }));
        Assert.Equal(0.5, KmsColorPipeline.HeadroomScale(
            ImageDescription.SdrDefault, WidePanel with { Luminances = (0, 160, 80) }), 6);
        Assert.Equal(1.0, KmsColorPipeline.HeadroomScale(ImageDescription.SdrDefault, WidePanel), 6);
    }

    [Fact]
    public void Edr_headroom_decomposes_to_the_bake()
    {
        var description = WidePanel with { Luminances = (0, 160, 80) };
        var decode = KmsColorPipeline.DecodeRamps(ImageDescription.SdrDefault, 4096);
        var encode = KmsColorPipeline.EncodeRamps(description, 4096);
        var ctm = KmsColorPipeline.GamutCtm(ImageDescription.SdrDefault, description);
        var scale = KmsColorPipeline.HeadroomScale(ImageDescription.SdrDefault, description);
        for (var i = 0; i < 9; i++)
        {
            ctm[i] *= scale;
        }

        const int size = 9;
        var lut = ColorLutBaker.Bake(ImageDescription.SdrDefault, description, size);
        var worst = 0.0;
        var worstBright = 0.0;
        var index = 0;
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    var (pr, pg, pb) = EvalPipeline(
                        decode, ctm, encode, r / (size - 1.0), g / (size - 1.0), b / (size - 1.0));
                    var error = Math.Max(
                        Math.Abs(pr - lut.Data[index]),
                        Math.Max(Math.Abs(pg - lut.Data[index + 1]), Math.Abs(pb - lut.Data[index + 2])));
                    worst = Math.Max(worst, error);
                    if (r >= 2 && g >= 2 && b >= 2)
                    {
                        worstBright = Math.Max(worstBright, error);
                    }

                    index += 3;
                }
            }
        }

        Assert.True(worst < 1.0 / 128, $"max channel error {worst} is over 1/128");
        Assert.True(worstBright < 1.0 / 256, $"max channel error away from black {worstBright} is over 1/256");
    }
}
