using System.Buffers.Binary;
using System.Diagnostics;
using Basin.Capabilities;
using Basin.Color;
using Basin.Diagnostics;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class HdrScreenshotTests
{
    private static readonly ImageDescription PqOutput = new()
    {
        PrimariesNamed = ColorPrimaries.Bt2020,
        TransferNamed = ColorTransferFunction.St2084Pq,
        Luminances = (0, 10000, 203),
    };

    private static readonly HdrStaticMetadata Mastering = new()
    {
        Eotf = HdrStaticMetadata.Transfer.Pq,
        PrimaryRed = (35400, 14600),
        PrimaryGreen = (8500, 39850),
        PrimaryBlue = (6550, 2300),
        WhitePoint = (15635, 16450),
        MaxMasteringLuminance = 1000,
        MinMasteringLuminance = 50,
        MaxContentLightLevel = 900,
        MaxFrameAverageLightLevel = 300,
    };

    [Fact]
    public void A_png16_carries_cicp_mdcv_and_clli_and_round_trips()
    {
        ushort[] rgb = [0, 1000, 65535, 32768, 12345, 54321, 1, 2, 3, 65535, 65535, 65535];
        var chunks = PngColorChunks.From(PqOutput, Mastering);
        Assert.Equal(new PngColorChunks(9, 16, Mastering), chunks);
        var png = PngCodec.Encode16(rgb, 2, 2, chunks);

        Assert.True(PngCodec.TryReadChunk(png, "cICP", out var cicp));
        Assert.Equal([9, 16, 0, 1], cicp);
        Assert.True(PngCodec.TryReadChunk(png, "mDCV", out var mdcv));
        Assert.Equal(24, mdcv.Length);
        Assert.Equal(35400, BinaryPrimitives.ReadUInt16BigEndian(mdcv));
        Assert.Equal(16450, BinaryPrimitives.ReadUInt16BigEndian(mdcv.AsSpan(14)));
        Assert.Equal(10_000_000u, BinaryPrimitives.ReadUInt32BigEndian(mdcv.AsSpan(16)));
        Assert.Equal(50u, BinaryPrimitives.ReadUInt32BigEndian(mdcv.AsSpan(20)));
        Assert.True(PngCodec.TryReadChunk(png, "cLLI", out var clli));
        Assert.Equal(9_000_000u, BinaryPrimitives.ReadUInt32BigEndian(clli));
        Assert.Equal(3_000_000u, BinaryPrimitives.ReadUInt32BigEndian(clli.AsSpan(4)));

        var (decoded, width, height) = PngCodec.Decode16(png);
        Assert.Equal((2, 2), (width, height));
        Assert.Equal(rgb, decoded);

        var sdr = PngCodec.Encode16(rgb, 2, 2, PngColorChunks.From(ImageDescription.SdrDefault, Mastering));
        Assert.True(PngCodec.TryReadChunk(sdr, "cICP", out var sdrCicp));
        Assert.Equal([1, 13, 0, 1], sdrCicp);
        Assert.False(PngCodec.TryReadChunk(sdr, "mDCV", out _));

        var pngcheck = new[] { "/usr/bin/pngcheck", "/usr/local/bin/pngcheck" }.FirstOrDefault(File.Exists);
        if (pngcheck is null)
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"basin-png16-{Environment.ProcessId}.png");
        File.WriteAllBytes(path, png);
        try
        {
            using var check = Process.Start(new ProcessStartInfo(pngcheck, ["-v", path]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            var output = check.StandardOutput.ReadToEnd() + check.StandardError.ReadToEnd();
            check.WaitForExit();
            Assert.True(check.ExitCode == 0, output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("vulkan")]
    [InlineData("gl")]
    public void A_half_float_capture_of_a_pq_output_keeps_values_above_sdr_white(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(64, 48, renderer);
        Assert.True(host.Renderer.RendersHalfFloat);
        host.SceneOutput.ColorDescription = PqOutput;
        host.SceneOutput.OutputHdrMetadata = Mastering;

        var content = new MemoryBuffer(64, 48, DrmFormat.Abgr16161616f);
        Assert.True(content.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            var four = BitConverter.HalfToUInt16Bits((Half)4f);
            var one = BitConverter.HalfToUInt16Bits((Half)1f);
            for (var y = 0; y < 48; y++)
            {
                var row = (ushort*)(view.Data + (y * view.Stride));
                for (var x = 0; x < 64; x++)
                {
                    row[x * 4] = four;
                    row[(x * 4) + 1] = four;
                    row[(x * 4) + 2] = four;
                    row[(x * 4) + 3] = one;
                }
            }
        }

        content.EndDataAccess();
        var node = new SceneBuffer(host.Scene.Root) { IsOpaque = true, ColorDescription = ImageDescription.WindowsScrgb() };
        node.SetBuffer(content);

        using var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black };
        var source = CaptureSource.Output(host.Output);
        Assert.True(capture.CanCaptureHalfFloat);
        Assert.True(capture.TryColorOf(source, out var color));
        Assert.Equal(new PngColorChunks(9, 16, Mastering), PngColorChunks.From(color.Description, color.Hdr));

        var wide = new MemoryBuffer(64, 48, DrmFormat.Abgr16161616f);
        var narrow = new MemoryBuffer(64, 48, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(capture.Capture(source, default, wide));
            Assert.True(BufferCapture.TryReadRgb16(wide, out var rgb));
            var expected = TransferCharacteristics.PqInverseEotf(320) * 65535;
            var sdrWhite = TransferCharacteristics.PqInverseEotf(80) * 65535;
            var sample = rgb[((24 * 64) + 32) * 3];
            Assert.True(sample > sdrWhite + 2000, $"{sample} is not above the SDR white code {sdrWhite:F0}");
            Assert.InRange(sample, expected - 1500, expected + 1500);

            Assert.True(capture.Capture(source, default, narrow));
            Assert.True(BufferCapture.TryReadRgba(narrow, out var rgba));
            var pqCode = TransferCharacteristics.PqInverseEotf(320) * 255;
            var narrowSample = rgba[((24 * 64) + 32) * 4];
            Assert.True(narrowSample > pqCode + 30, $"an 8-bit capture of an HDR output is tone-mapped to SDR, not the PQ code {pqCode:F0}: {narrowSample}");
        }
        finally
        {
            wide.Destroy();
            narrow.Destroy();
            content.Destroy();
        }
    }
}
