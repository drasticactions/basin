using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;
using Basin.Desktop.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class ColorMathTests
{
    private static readonly ImageDescription SrgbDescription = ImageDescription.Srgb;

    private static readonly ImageDescription PqDescription = new()
    {
        PrimariesNamed = ColorPrimaries.Bt2020,
        TransferNamed = ColorTransferFunction.St2084Pq,
        MasteringLuminance = (1, 1000),
    };

    [Fact]
    public void Transfer_curves_roundtrip()
    {
        foreach (var tf in (ColorTransferFunction[])
            [ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22,
             ColorTransferFunction.ExtLinear, ColorTransferFunction.St2084Pq,
             ColorTransferFunction.Hlg, ColorTransferFunction.CompoundPower24])
        {
            var tc = TransferCharacteristics.From(new ImageDescription
            {
                PrimariesNamed = ColorPrimaries.Srgb,
                TransferNamed = tf,
            });
            for (var i = 0; i <= 16; i++)
            {
                var signal = i / 16.0;
                Assert.Equal(signal, tc.Encode(tc.Decode(signal)), 5);
            }
        }
    }

    [Fact]
    public void Pq_hits_its_reference_values()
    {
        Assert.Equal(10000, TransferCharacteristics.PqEotf(1.0), 3);
        Assert.Equal(0.5806888810416109, TransferCharacteristics.PqInverseEotf(203), 6);
        Assert.Equal(0, TransferCharacteristics.PqEotf(0), 6);
    }

    [Fact]
    public void The_compound_curve_is_the_one_the_deprecated_srgb_entry_left_ambiguous()
    {
        var compound = Characteristics(ColorTransferFunction.CompoundPower24);
        var srgb = Characteristics(ColorTransferFunction.Srgb);
        var gamma22 = Characteristics(ColorTransferFunction.Gamma22);

        var apart = 0;
        for (var i = 0; i <= 16; i++)
        {
            var signal = i / 16.0;
            Assert.Equal(srgb.Decode(signal), compound.Decode(signal), 9);
            if (Math.Abs(gamma22.Decode(signal) - compound.Decode(signal)) > 1e-6)
            {
                apart++;
            }
        }

        Assert.True(apart > 8, "gamma22 and compound_power_2_4 must not be the same curve");

        Assert.Equal(0.04 / 12.92, TransferCharacteristics.Compound24Eotf(0.04), 9);
        Assert.Equal(Math.Pow((0.5 + 0.055) / 1.055, 2.4), TransferCharacteristics.Compound24Eotf(0.5), 9);
    }

    [Fact]
    public void Absolute_no_adaptation_leaves_the_white_points_where_they_are()
    {
        var adapted = Colorimetry.GamutMatrix(Chromaticities.DciP3, Chromaticities.Srgb);
        var unadapted = Colorimetry.GamutMatrix(Chromaticities.DciP3, Chromaticities.Srgb, adaptWhite: false);

        double[] adaptedWhite = Colorimetry.Apply(adapted, [1.0, 1, 1]);
        Assert.Equal(1, adaptedWhite[0], 6);
        Assert.Equal(1, adaptedWhite[1], 6);
        Assert.Equal(1, adaptedWhite[2], 6);

        double[] keptWhite = Colorimetry.Apply(unadapted, [1.0, 1, 1]);
        Assert.False(
            Math.Abs(keptWhite[0] - 1) < 1e-3 && Math.Abs(keptWhite[2] - 1) < 1e-3,
            "an unadapted DCI-P3 white must not land on the output's white");

        var sameWhite = Colorimetry.GamutMatrix(Chromaticities.DisplayP3, Chromaticities.Srgb, adaptWhite: false);
        var sameWhiteAdapted = Colorimetry.GamutMatrix(Chromaticities.DisplayP3, Chromaticities.Srgb);
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(sameWhiteAdapted[i], sameWhite[i], 9);
        }
    }

    [Fact]
    public void The_bakers_intent_reaches_the_matrix()
    {
        var p3 = new ImageDescription
        {
            PrimariesNamed = ColorPrimaries.DciP3,
            TransferNamed = ColorTransferFunction.CompoundPower24,
        };

        var perceptual = ColorLutBaker.Bake(p3, SrgbDescription, 5);
        var noAdaptation = ColorLutBaker.Bake(p3, SrgbDescription, 5, ColorRenderIntent.AbsoluteNoAdaptation);
        var top = perceptual.Data.Length - 3;
        Assert.NotEqual(perceptual.Data[top], noAdaptation.Data[top], 3);
    }

    private static TransferCharacteristics Characteristics(ColorTransferFunction tf) =>
        TransferCharacteristics.From(new ImageDescription
        {
            PrimariesNamed = ColorPrimaries.Srgb,
            TransferNamed = tf,
        });

    [Fact]
    public void Gamut_matrix_between_equal_spaces_is_identity()
    {
        var m = Colorimetry.GamutMatrix(Chromaticities.Srgb, Chromaticities.Srgb);
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(i % 4 == 0 ? 1 : 0, m[i], 9);
        }
    }

    [Fact]
    public void Srgb_red_lands_inside_bt2020()
    {
        var m = Colorimetry.GamutMatrix(Chromaticities.Srgb, Chromaticities.Bt2020);
        double[] red = Colorimetry.Apply(m, [1.0, 0, 0]);
        Assert.InRange(red[0], 0.5, 1);
        Assert.InRange(red[1], 0, 0.2);
        Assert.InRange(red[2], 0, 0.2);
        double[] white = Colorimetry.Apply(m, [1.0, 1, 1]);
        Assert.Equal(1, white[0], 6);
        Assert.Equal(1, white[1], 6);
        Assert.Equal(1, white[2], 6);
    }

    [Fact]
    public void Srgb_to_srgb_lut_is_identity()
    {
        Assert.True(ColorLutBaker.IsIdentity(SrgbDescription, SrgbDescription));
        var lut = ColorLutBaker.Bake(SrgbDescription, SrgbDescription, 9);
        var n = lut.Size;
        for (var b = 0; b < n; b++)
        {
            for (var g = 0; g < n; g++)
            {
                for (var r = 0; r < n; r++)
                {
                    var at = (((b * n) + g) * n + r) * 3;
                    Assert.Equal(r / (double)(n - 1), lut.Data[at], 3);
                    Assert.Equal(g / (double)(n - 1), lut.Data[at + 1], 3);
                    Assert.Equal(b / (double)(n - 1), lut.Data[at + 2], 3);
                }
            }
        }
    }

    [Fact]
    public void Sdr_white_lands_at_graphics_white_on_a_pq_output()
    {
        var lut = ColorLutBaker.Bake(SrgbDescription, PqDescription, 5);
        var top = lut.Data.Length - 3;
        Assert.Equal(0.5807, lut.Data[top], 2);
        Assert.Equal(0.5807, lut.Data[top + 1], 2);
        Assert.Equal(0.5807, lut.Data[top + 2], 2);
        Assert.Equal(0, lut.Data[0], 3);
    }

    [Fact]
    public void Hdr_to_sdr_tone_maps_monotonically_and_preserves_reference_white()
    {
        var lut = ColorLutBaker.Bake(PqDescription, SrgbDescription, 17);
        var n = lut.Size;
        var previous = -1.0;
        for (var i = 0; i < n; i++)
        {
            var gray = (((i * n) + i) * n + i) * 3;
            var value = lut.Data[gray];
            Assert.True(value >= previous - 1e-4, $"gray ramp not monotonic at {i}: {value} < {previous}");
            previous = value;
        }

        var refIndex = (int)Math.Round(0.5807 * (n - 1));
        var at = (((refIndex * n) + refIndex) * n + refIndex) * 3;
        Assert.InRange(lut.Data[at], 0.8, 1.0);
    }

    [Fact]
    public void Icc_srgb_profile_bakes_a_near_identity_lut()
    {
        Assert.SkipUnless(Lcms2Support.IsAvailable, "liblcms2 ≥ 2.19 not present");
        byte[] icc;
        using (var srgb = Lcms2.IccProfile.CreateSrgb())
        {
            icc = srgb.SaveToArray();
        }

        var lut = ColorLutBaker.BakeFromIcc(icc, SrgbDescription, 9);
        Assert.NotNull(lut);
        var n = lut.Size;
        for (var i = 0; i < n; i++)
        {
            var at = (((i * n) + i) * n + i) * 3;
            Assert.Equal(i / (double)(n - 1), lut.Data[at], tolerance: 0.02);
        }

        Assert.Null(ColorLutBaker.BakeFromIcc([1, 2, 3, 4], SrgbDescription, 9));
    }

    [Fact]
    public void Output_descriptions_derive_from_display_facts()
    {
        var chroma = (0.68, 0.32, 0.2646, 0.68, 0.1504, 0.0596, 0.3135, 0.3291);

        var sdr = OutputDescriptions.Sdr(chroma);
        Assert.NotNull(sdr.PrimariesCustom);
        Assert.Equal(680000, sdr.PrimariesCustom!.Value.Rx);
        Assert.Equal(ColorTransferFunction.Srgb, sdr.TransferNamed);
        Assert.Same(ImageDescription.Srgb, OutputDescriptions.Sdr(null));

        var hdr = OutputDescriptions.Hdr10(496.7, 0.0001);
        Assert.Equal(ColorPrimaries.Bt2020, hdr.PrimariesNamed);
        Assert.Equal(ColorTransferFunction.St2084Pq, hdr.TransferNamed);
        Assert.Equal(496u, hdr.Luminances!.Value.Max);
        Assert.Equal(203u, hdr.Luminances.Value.Reference);

        var metadata = OutputDescriptions.HdrMetadataFor(hdr, chroma);
        Assert.Equal(HdrStaticMetadata.Transfer.Pq, metadata.Eotf);
        Assert.Equal(34000, metadata.PrimaryRed.X);
        Assert.Equal(16000, metadata.PrimaryRed.Y);
        Assert.Equal(496, metadata.MaxMasteringLuminance);
    }

    [Fact]
    public void Night_light_is_identity_at_6500K_and_warms_below()
    {
        var (r, g, b) = NightLight.Multipliers(6500);
        Assert.Equal(1, r, 2);
        Assert.InRange(g, 0.97, 1.0);
        Assert.InRange(b, 0.97, 1.0);

        var (wr, wg, wb) = NightLight.Multipliers(3500);
        Assert.Equal(1, wr, 3);
        Assert.True(wg < 0.9, $"green should drop at 3500K, got {wg}");
        Assert.True(wb < 0.7, $"blue should drop hard at 3500K, got {wb}");

        Span<ushort> red = stackalloc ushort[256];
        Span<ushort> green = stackalloc ushort[256];
        Span<ushort> blue = stackalloc ushort[256];
        NightLight.FillGammaRamps(3500, red, green, blue);
        Assert.Equal(0, red[0]);
        Assert.Equal(ushort.MaxValue, red[255]);
        Assert.True(blue[255] < ushort.MaxValue * 0.7);
    }
}
