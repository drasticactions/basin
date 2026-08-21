using System.Diagnostics;
using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Video.FFmpeg;
using Xunit;

namespace Basin.Tests;

public sealed class FFmpegDecoderTests
{
    private static readonly Lazy<bool> HasFFmpegCli = new(() =>
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            probe!.WaitForExit(10_000);
            return probe.ExitCode == 0;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    });

    private static FFmpegVideoDecoder RequireDecoder()
    {
        var decoder = FFmpegVideoDecoder.TryCreate(out var whyNot);
        Assert.SkipWhen(decoder is null, $"no usable system FFmpeg: {whyNot}");
        return decoder!;
    }

    private static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)(x * 255 / width);
                pixels[offset + 1] = (byte)(y * 255 / height);
                pixels[offset + 2] = (byte)(128 + (x * 64 / width));
                pixels[offset + 3] = 0xff;
            }
        }

        return pixels;
    }

    private static byte[] EncodeH264(byte[] bgra, int width, int height)
    {
        var directory = Directory.CreateTempSubdirectory("basin-ffmpeg-test");
        try
        {
            var input = Path.Combine(directory.FullName, "in.raw");
            var output = Path.Combine(directory.FullName, "out.h264");
            File.WriteAllBytes(input, bgra);
            var info = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("-y");
            info.ArgumentList.Add("-f");
            info.ArgumentList.Add("rawvideo");
            info.ArgumentList.Add("-pix_fmt");
            info.ArgumentList.Add("bgra");
            info.ArgumentList.Add("-s");
            info.ArgumentList.Add($"{width}x{height}");
            info.ArgumentList.Add("-i");
            info.ArgumentList.Add(input);
            info.ArgumentList.Add("-frames:v");
            info.ArgumentList.Add("1");
            info.ArgumentList.Add("-vf");
            info.ArgumentList.Add("scale=out_color_matrix=bt601:out_range=tv,format=yuv420p");
            info.ArgumentList.Add("-c:v");
            info.ArgumentList.Add("libx264");
            info.ArgumentList.Add("-preset");
            info.ArgumentList.Add("ultrafast");
            info.ArgumentList.Add("-tune");
            info.ArgumentList.Add("zerolatency");
            info.ArgumentList.Add("-qp");
            info.ArgumentList.Add("4");
            info.ArgumentList.Add("-f");
            info.ArgumentList.Add("h264");
            info.ArgumentList.Add(output);
            using var encode = Process.Start(info)!;
            var noise = encode.StandardError.ReadToEnd();
            encode.WaitForExit(30_000);
            Assert.SkipWhen(encode.ExitCode != 0, $"the system ffmpeg cannot encode H.264: {noise}");
            return File.ReadAllBytes(output);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void An_h264_round_trip_stays_inside_the_limited_range_tolerance()
    {
        var decoder = RequireDecoder();
        Assert.SkipWhen(!HasFFmpegCli.Value, "no ffmpeg executable to encode the reference stream with");
        Assert.SkipWhen(!decoder.Supports(VideoCodec.H264), "the system FFmpeg decodes no H.264");

        const int width = 64, height = 64, stride = width * 4;
        var source = Gradient(width, height);
        var packet = EncodeH264(source, width, height);

        using var session = decoder.Open(VideoCodec.H264, width, height, DrmFormat.Xrgb8888);
        var decoded = new byte[height * stride];
        unsafe
        {
            fixed (byte* destination = decoded)
            {
                Assert.True(session.Decode(packet, (nint)destination, stride));
                Assert.True(session.Decode(packet, (nint)destination, stride));
            }
        }

        var worst = 0;
        for (var i = 0; i < decoded.Length; i++)
        {
            if (i % 4 == 3)
            {
                continue;
            }

            worst = Math.Max(worst, Math.Abs(decoded[i] - source[i]));
        }

        Assert.True(worst <= 16, $"the worst per-channel error is {worst}");
    }

    [Fact]
    public void A_padded_frame_is_cropped_and_never_scaled()
    {
        var decoder = RequireDecoder();
        Assert.SkipWhen(!HasFFmpegCli.Value, "no ffmpeg executable to encode the reference stream with");
        Assert.SkipWhen(!decoder.Supports(VideoCodec.H264), "the system FFmpeg decodes no H.264");

        const int bufferWidth = 118, bufferHeight = 70;
        const int alignedWidth = 128, alignedHeight = 80;
        const int stride = bufferWidth * 4;
        var aligned = Gradient(alignedWidth, alignedHeight);
        var packet = EncodeH264(aligned, alignedWidth, alignedHeight);

        using var session = decoder.Open(VideoCodec.H264, bufferWidth, bufferHeight, DrmFormat.Xrgb8888);
        var decoded = new byte[bufferHeight * stride];
        unsafe
        {
            fixed (byte* destination = decoded)
            {
                Assert.True(session.Decode(packet, (nint)destination, stride));
            }
        }

        for (var y = 0; y < bufferHeight; y += 7)
        {
            var source = ((y * alignedWidth) + (bufferWidth - 1)) * 4;
            var target = (y * stride) + ((bufferWidth - 1) * 4);
            Assert.True(
                Math.Abs(decoded[target] - aligned[source]) <= 20,
                $"the right edge drifted at row {y}: {decoded[target]} vs {aligned[source]}");
        }

        for (var x = 0; x < bufferWidth; x += 7)
        {
            var source = (((bufferHeight - 1) * alignedWidth) + x) * 4;
            var target = ((bufferHeight - 1) * stride) + (x * 4);
            Assert.True(
                Math.Abs(decoded[target + 1] - aligned[source + 1]) <= 20,
                $"the bottom edge drifted at column {x}: {decoded[target + 1]} vs {aligned[source + 1]}");
        }
    }

    [Fact]
    public void An_h264_round_trip_decodes_on_hardware_when_a_device_opens()
    {
        var decoder = FFmpegVideoDecoder.TryCreate(preferHardware: true, out var whyNot);
        Assert.SkipWhen(decoder is null, $"no usable system FFmpeg: {whyNot}");
        Assert.SkipWhen(decoder!.HardwareName is null, "no hardware decode device opens on this box");
        Assert.SkipWhen(!HasFFmpegCli.Value, "no ffmpeg executable to encode the reference stream with");
        Assert.SkipWhen(!decoder.Supports(VideoCodec.H264), "the system FFmpeg decodes no H.264");

        const int width = 128, height = 80, stride = width * 4;
        var source = Gradient(width, height);
        var packet = EncodeH264(source, width, height);

        using var session = decoder.Open(VideoCodec.H264, width, height, DrmFormat.Xrgb8888);
        var decoded = new byte[height * stride];
        unsafe
        {
            fixed (byte* destination = decoded)
            {
                Assert.True(session.Decode(packet, (nint)destination, stride));
                Assert.True(session.Decode(packet, (nint)destination, stride));
            }
        }

        Assert.True(
            ((FFmpegDecodeSession)session).DecodedOnHardware,
            $"the {decoder.HardwareName} device opened and the stream still decoded in software");

        var worst = 0;
        for (var i = 0; i < decoded.Length; i++)
        {
            if (i % 4 == 3)
            {
                continue;
            }

            worst = Math.Max(worst, Math.Abs(decoded[i] - source[i]));
        }

        Assert.True(worst <= 20, $"the worst per-channel error is {worst}");
    }

    [Fact]
    public void A_session_survives_a_packet_of_garbage()
    {
        var decoder = RequireDecoder();
        Assert.SkipWhen(!decoder.Supports(VideoCodec.H264), "the system FFmpeg decodes no H.264");

        using var session = decoder.Open(VideoCodec.H264, 16, 16, DrmFormat.Xrgb8888);
        var junk = new byte[64];
        Random.Shared.NextBytes(junk);
        var decoded = new byte[16 * 64];
        unsafe
        {
            fixed (byte* destination = decoded)
            {
                Assert.False(session.Decode(junk, (nint)destination, 64));
            }
        }
    }

    [Fact]
    public void The_decoder_reports_what_it_supports_without_opening_anything()
    {
        var decoder = RequireDecoder();
        _ = decoder.Supports(VideoCodec.H264);
        _ = decoder.Supports(VideoCodec.Vp9);
        _ = decoder.Supports(VideoCodec.Av1);
    }
}
