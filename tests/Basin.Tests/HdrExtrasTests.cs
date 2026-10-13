using System.Diagnostics;
using System.Text;
using Basin.Capabilities;
using Basin.Color;
using Basin.Desktop;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class HdrExtrasTests
{
    private static readonly ImageDescription PqOutput = Basin.Color.OutputDescriptions.Hdr10(1000, 0);

    private static double[] Expand(double r, double g, double b, double sdrNits, double targetNits)
    {
        double[] rgb = [r, g, b];
        Bt2446a.Expand(rgb, sdrNits, targetNits);
        return rgb;
    }

    [Fact]
    public void Inverse_tone_mapping_defaults_to_gamescopes_tunables()
    {
        Assert.Equal(100, new InverseToneMapping().SdrNits);
        Assert.Equal(1000, new InverseToneMapping().TargetNits);
        Assert.All(Expand(1, 1, 1, 0, 0), value => Assert.False(double.IsNaN(value)));
    }

    [Fact]
    public void Bt2446a_takes_black_to_black_and_white_to_the_tables_peak()
    {
        Assert.All(Expand(0, 0, 0, 100, 1000), value => Assert.Equal(0, value, 6));
        var white = Expand(1, 1, 1, 100, 1000);
        var expected = Math.Pow(Math.Pow(255, (2.8305e-6 * 255 * 255) - (7.4622e-4 * 255) + 1.2328) / 1000, 2.4) * 1000;
        Assert.All(white, value => Assert.Equal(expected, value, 3));
        Assert.All(Expand(1, 1, 1, 203, 1000), value => Assert.Equal(1000, value, 3));
    }

    [Theory]
    [InlineData(100, 1000)]
    [InlineData(203, 1000)]
    [InlineData(100, 600)]
    public void Bt2446a_rises_with_the_signal(double sdrNits, double targetNits)
    {
        var previous = -1.0;
        for (var step = 0; step <= 64; step++)
        {
            var grey = step / 64.0;
            var expanded = Expand(grey, grey, grey, sdrNits, targetNits);
            Assert.True(expanded[1] >= previous, $"{grey}: {expanded[1]} after {previous}");
            Assert.InRange(expanded[1], 0, targetNits + 0.001);
            previous = expanded[1];
        }
    }

    [Fact]
    public void Inverse_tone_mapping_lifts_sdr_white_on_a_pq_output_only()
    {
        var output = PqOutput with { InverseToneMapping = new InverseToneMapping() };
        var lifted = ColorLutBaker.Bake(ImageDescription.SdrDefault, output, 9);
        var anchored = ColorLutBaker.Bake(ImageDescription.SdrDefault, PqOutput, 9);
        var white = lifted.Data.Length - 3;
        Assert.True(lifted.Data[white + 1] > anchored.Data[white + 1] + 0.05f);

        var declining = ImageDescription.SdrDefault with { DeclinesInverseToneMapping = true };
        var declined = ColorLutBaker.Bake(declining, output, 9);
        Assert.Equal(anchored.Data[white + 1], declined.Data[white + 1], 4);

        var pqSource = ColorLutBaker.Bake(PqOutput with { Luminances = null }, output, 9);
        var pqPlain = ColorLutBaker.Bake(PqOutput with { Luminances = null }, PqOutput, 9);
        Assert.Equal(pqPlain.Data[white + 1], pqSource.Data[white + 1], 4);
    }

    [Fact]
    public void A_decomposed_renderer_takes_the_baked_table_only_while_the_output_needs_one()
    {
        using var host = new CompositorTestHost(renderer: "pixman");
        Assert.False(PqOutput.NeedsBakedTable);
        Assert.True((PqOutput with { InverseToneMapping = new InverseToneMapping() }).NeedsBakedTable);
        Assert.False(ColorLutBaker.IsIdentity(ImageDescription.SdrDefault, ImageDescription.SdrDefault with { Look = Identity(2) }));
    }

    [Fact]
    public void The_soft_blend_keeps_a_saturated_wide_color_off_the_hard_clip()
    {
        var bt2020 = new ImageDescription { PrimariesNamed = ColorPrimaries.Bt2020, TransferNamed = ColorTransferFunction.Gamma22 };
        var hard = ColorTransformParameters.From(bt2020, ImageDescription.SdrDefault, ColorRenderIntent.Perceptual, softGamut: false);
        var soft = ColorTransformParameters.From(bt2020, ImageDescription.SdrDefault, ColorRenderIntent.Perceptual, softGamut: true);
        Assert.True(soft.SoftGamut);

        double[] mint = [0.5, 1, 0.5];
        double[] clipped = [0.5, 1, 0.5];
        soft.Apply(mint);
        hard.Apply(clipped);
        Assert.True(
            Math.Abs(mint[0] - clipped[0]) + Math.Abs(mint[1] - clipped[1]) + Math.Abs(mint[2] - clipped[2]) > 0.01,
            $"soft {string.Join(',', mint)} hard {string.Join(',', clipped)}");

        double[] grey = [0.5, 0.5, 0.5];
        double[] greyHard = [0.5, 0.5, 0.5];
        soft.Apply(grey);
        hard.Apply(greyHard);
        Assert.Equal(greyHard, grey);

        var narrow = ColorTransformParameters.From(ImageDescription.SdrDefault, bt2020 with { }, ColorRenderIntent.Perceptual, softGamut: true);
        Assert.False(narrow.SoftGamut);
    }

    private static ColorLut3D Identity(int size)
    {
        var data = new float[size * size * size * 3];
        var index = 0;
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    data[index++] = r / (float)(size - 1);
                    data[index++] = g / (float)(size - 1);
                    data[index++] = b / (float)(size - 1);
                }
            }
        }

        return new ColorLut3D(size, data);
    }

    private static string Cube(int size, Func<float, float, float, (float, float, float)> map, string header = "")
    {
        var text = new StringBuilder("# a test look\nTITLE \"t\"\n").Append(header).Append($"LUT_3D_SIZE {size}\n");
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    var (x, y, z) = map(r / (float)(size - 1), g / (float)(size - 1), b / (float)(size - 1));
                    text.Append(FormattableString.Invariant($"{x} {y} {z}\n"));
                }
            }
        }

        return text.ToString();
    }

    [Fact]
    public void A_cube_file_parses_red_fastest_and_folds_into_the_bake()
    {
        var look = CubeLut.Parse(Encoding.UTF8.GetBytes(Cube(3, (r, g, b) => (g, b, r))));
        Assert.Equal(3, look.Size);
        Assert.Equal((0.5f, 1f, 0f), look.SampleTetrahedral(0, 0.5f, 1));

        var output = ImageDescription.SdrDefault with { Look = look };
        var baked = ColorLutBaker.Bake(ImageDescription.SdrDefault, output, 3);
        var index = ((2 * 3) + 1) * 3 * 3;
        Assert.Equal(0.5f, baked.Data[index], 3);
        Assert.Equal(1f, baked.Data[index + 1], 3);
        Assert.Equal(0f, baked.Data[index + 2], 3);
    }

    [Fact]
    public void A_cube_domain_is_folded_into_the_unit_range()
    {
        var look = CubeLut.Parse(Encoding.UTF8.GetBytes(
            Cube(2, (r, g, b) => (r, g, b), "DOMAIN_MIN 0 0 0\nDOMAIN_MAX 2 2 2\n")));
        var (r, g, b) = look.SampleTetrahedral(0.5f, 0.5f, 0.5f);
        Assert.Equal(0.25f, r, 4);
        Assert.Equal(0.25f, g, 4);
        Assert.Equal(0.25f, b, 4);
    }

    [Theory]
    [InlineData("LUT_1D_SIZE 4\n0 0 0\n", "1D LUT")]
    [InlineData("LUT_3D_SIZE 2\n0 0 0\n1 1 1\n", "2 rows")]
    [InlineData("LUT_3D_SIZE 1\n", "LUT_3D_SIZE")]
    [InlineData("0 0 0\n", "before LUT_3D_SIZE")]
    [InlineData("LUT_3D_SIZE 2\n0 0 x\n", "not a number")]
    public void A_cube_file_that_is_not_a_3D_look_is_refused_by_name(string text, string reason)
    {
        var error = Assert.Throws<FormatException>(() => CubeLut.Parse(Encoding.UTF8.GetBytes(text)));
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Hdr_demand_follows_a_pq_window_and_holds_through_the_release_delay()
    {
        using var host = new CompositorTestHost();
        var toplevels = new TestToplevelModel();
        var window = MappedToplevel.Map(host, host.Client);
        var descriptions = new Dictionary<Surface, ImageDescription>();
        var surface = window.ServerSurface;
        using var demand = new HdrDemand(toplevels, host.Loop, s => descriptions.GetValueOrDefault(s)) { ReleaseDelayMs = 150 };
        var changes = new List<bool>();
        demand.Changed += changes.Add;

        var id = toplevels.Add("game", "game", surface);
        Assert.False(demand.Demand);

        descriptions[surface] = PqOutput;
        demand.Reevaluate();
        Assert.True(demand.Demand);
        Assert.Equal([true], changes);

        toplevels.Remove(id);
        Assert.True(demand.Demand);
        var clock = Stopwatch.StartNew();
        while (demand.Demand && clock.ElapsedMilliseconds < 2000)
        {
            host.Loop.Dispatch(10);
        }

        Assert.False(demand.Demand);
        Assert.InRange(clock.ElapsedMilliseconds, 100, 2000);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void A_window_that_comes_back_inside_the_delay_keeps_the_demand()
    {
        using var host = new CompositorTestHost();
        var toplevels = new TestToplevelModel();
        var window = MappedToplevel.Map(host, host.Client);
        var surface = window.ServerSurface;
        using var demand = new HdrDemand(toplevels, host.Loop, _ => PqOutput) { ReleaseDelayMs = 300 };
        var changes = new List<bool>();
        demand.Changed += changes.Add;
        var id = toplevels.Add("game", "game", surface);
        toplevels.Remove(id);
        host.Loop.Dispatch(50);
        toplevels.Add("game", "game", surface);
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 500)
        {
            host.Loop.Dispatch(20);
        }

        Assert.True(demand.Demand);
        Assert.Equal([true], changes);
    }

    [Fact]
    public void Content_metadata_comes_from_a_pq_description_with_mastering_values()
    {
        Assert.Null(HdrStaticMetadata.FromContent(PqOutput with { MaxCll = null, MaxFall = null, MasteringLuminance = null }));
        Assert.Null(HdrStaticMetadata.FromContent(ImageDescription.SdrDefault with { MaxCll = 400 }));
        var metadata = HdrStaticMetadata.FromContent(new ImageDescription
        {
            TransferNamed = ColorTransferFunction.St2084Pq,
            PrimariesNamed = ColorPrimaries.Bt2020,
            MasteringLuminance = (50, 4000),
            MaxCll = 1500,
            MaxFall = 400,
        });
        Assert.NotNull(metadata);
        Assert.Equal(4000, metadata.Value.MaxMasteringLuminance);
        Assert.Equal(50, metadata.Value.MinMasteringLuminance);
        Assert.Equal(1500, metadata.Value.MaxContentLightLevel);
        Assert.Equal(400, metadata.Value.MaxFrameAverageLightLevel);
        Assert.Equal((35400, 14600), metadata.Value.PrimaryRed);
        Assert.Equal((15635, 16450), metadata.Value.WhitePoint);
    }

    [Fact]
    public void Scanout_passes_a_pq_surfaces_metadata_through_and_composition_restores_the_outputs()
    {
        using var host = new CompositorTestHost();
        var outputMetadata = new HdrStaticMetadata { Eotf = HdrStaticMetadata.Transfer.Pq, MaxMasteringLuminance = 600 };
        using var sceneOutput = new SceneOutput(host.Scene, host.Output)
        {
            ScanoutEntryThreshold = 1,
            HdrMetadataPassthrough = true,
            OutputHdrMetadata = outputMetadata,
        };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var client = DirectScanoutTests.FakeClientBuffer(160, 120);
        var node = new SceneBuffer(host.Scene.Root)
        {
            IsOpaque = true,
            ColorDescription = PqOutput with { MaxCll = 900, MaxFall = 300 },
        };
        node.SetBuffer(client);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);
        Assert.True((state.Fields & OutputStateFields.Hdr) != 0);
        Assert.Equal(900, state.Hdr!.Value.MaxContentLightLevel);
        Assert.Equal(sceneOutput.ContentHdrMetadata, state.Hdr);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True((state.Fields & OutputStateFields.Hdr) == 0, "unchanged metadata is not staged again");

        var cover = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 0f, 0f, 1f));
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);
        Assert.True((state.Fields & OutputStateFields.Hdr) != 0);
        Assert.Equal(outputMetadata, state.Hdr);
        Assert.Null(sceneOutput.ContentHdrMetadata);

        cover.Destroy();
        node.Destroy();
        client.Destroy();
    }

    [Fact]
    public void Without_passthrough_scanout_never_touches_the_metadata()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output) { ScanoutEntryThreshold = 1 };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var client = DirectScanoutTests.FakeClientBuffer(160, 120);
        var node = new SceneBuffer(host.Scene.Root) { IsOpaque = true, ColorDescription = PqOutput with { MaxCll = 900 } };
        node.SetBuffer(client);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);
        Assert.True((state.Fields & OutputStateFields.Hdr) == 0);

        node.Destroy();
        client.Destroy();
    }
}
