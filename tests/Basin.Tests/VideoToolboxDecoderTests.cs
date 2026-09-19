using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Basin.Capabilities;
using Basin.Video.MediaCodec;
using Basin.Video.VideoToolbox;
using Basin.Video.WebCodecs;
using Xunit;

namespace Basin.Tests;

[SupportedOSPlatform("macos")]
public sealed class VideoToolboxDecoderTests
{
    private static VideoToolboxVideoDecoder RequireDecoder()
    {
        Assert.SkipWhen(!OperatingSystem.IsMacOS(), "VideoToolbox exists only on Apple hosts");
        var decoder = VideoToolboxVideoDecoder.TryCreate(out var whyNot);
        Assert.SkipWhen(decoder is null, $"no usable VideoToolbox: {whyNot}");
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

    [Fact]
    public void Absence_is_reported_by_name_where_the_framework_does_not_load()
    {
        if (OperatingSystem.IsMacOS())
        {
            Assert.NotNull(VideoToolboxVideoDecoder.TryCreate(out var whyNot));
            Assert.Null(whyNot);
            return;
        }

        Assert.Null(VideoToolboxVideoDecoder.TryCreate(out var reason));
        Assert.Contains("framework", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_other_platform_decoders_report_absence_by_name_on_this_host()
    {
        Assert.SkipWhen(OperatingSystem.IsAndroid(), "libmediandk loads here");
        Assert.SkipWhen(OperatingSystem.IsBrowser(), "WebCodecs exists here");
#pragma warning disable CA1416
        Assert.Null(MediaCodecVideoDecoder.TryCreate(out var mediaCodec));
        Assert.Contains("libmediandk.so", mediaCodec, StringComparison.Ordinal);

        Assert.Null(await WebCodecsVideoDecoder.TryCreateAsync());
        Assert.Contains("browser", WebCodecsVideoDecoder.WhyNot, StringComparison.Ordinal);
#pragma warning restore CA1416
    }

    [Fact]
    public void H264_is_supported_wherever_the_framework_loads_and_the_others_are_answered()
    {
        var decoder = RequireDecoder();
        Assert.True(decoder.Supports(VideoCodec.H264));
        _ = decoder.Supports(VideoCodec.Vp9);
        _ = decoder.Supports(VideoCodec.Av1);
    }

    [Fact]
    public void A_session_opens_and_closes_without_a_frame()
    {
        var decoder = RequireDecoder();
        using var session = decoder.Open(VideoCodec.H264, 16, 8, DrmFormat.Xrgb8888);
        var destination = new byte[16 * 8 * 4];
        unsafe
        {
            fixed (byte* pixels = destination)
            {
                Assert.False(session.Decode([0, 0, 0, 1, 0x65, 0x88], (nint)pixels, 64));
            }
        }
    }

    [Fact]
    public void An_h264_round_trip_stays_inside_the_limited_range_tolerance()
    {
        var decoder = RequireDecoder();
        const int width = 64, height = 64, stride = width * 4;
        var source = Gradient(width, height);
        var packet = VtEncoder.EncodeH264(source, width, height);
        Assert.SkipWhen(packet is null, "VideoToolbox on this host encodes no H.264");

        using var session = decoder.Open(VideoCodec.H264, width, height, DrmFormat.Xrgb8888);
        var decoded = new byte[height * stride];
        unsafe
        {
            fixed (byte* destination = decoded)
            {
                Assert.True(session.Decode(packet!, (nint)destination, stride));
                Assert.True(session.Decode(packet!, (nint)destination, stride));
            }
        }

        var worst = 0;
        var worstAt = 0;
        var sum = 0L;
        for (var i = 0; i < decoded.Length; i++)
        {
            var x = i / 4 % width;
            var y = i / 4 / width;
            if (i % 4 == 3 || x == 0 || y == 0 || x == width - 1 || y == height - 1)
            {
                continue;
            }

            var error = Math.Abs(decoded[i] - source[i]);
            sum += error;
            if (error > worst)
            {
                worst = error;
                worstAt = i;
            }
        }

        var mean = sum / (3L * (width - 2) * (height - 2));
        Assert.True(mean <= 4, $"the mean per-channel error is {mean}");
        Assert.True(
            worst <= 24,
            $"the worst interior per-channel error is {worst} at x={worstAt / 4 % width} y={worstAt / 4 / width} "
            + $"channel {worstAt % 4} (decoded {decoded[worstAt]} vs {source[worstAt]})");
    }

    [Fact]
    public void A_padded_frame_is_cropped_and_never_scaled()
    {
        var decoder = RequireDecoder();
        const int bufferWidth = 118, bufferHeight = 70;
        const int alignedWidth = 128, alignedHeight = 80;
        const int stride = bufferWidth * 4;
        var aligned = Gradient(alignedWidth, alignedHeight);
        var packet = VtEncoder.EncodeH264(aligned, alignedWidth, alignedHeight);
        Assert.SkipWhen(packet is null, "VideoToolbox on this host encodes no H.264");

        using var session = decoder.Open(VideoCodec.H264, bufferWidth, bufferHeight, DrmFormat.Xrgb8888);
        var decoded = new byte[bufferHeight * stride];
        unsafe
        {
            fixed (byte* destination = decoded)
            {
                Assert.True(session.Decode(packet!, (nint)destination, stride));
            }
        }

        for (var y = 0; y < bufferHeight; y += 7)
        {
            var source = ((y * alignedWidth) + (bufferWidth - 1)) * 4;
            var target = (y * stride) + ((bufferWidth - 1) * 4);
            Assert.True(
                Math.Abs(decoded[target] - aligned[source]) <= 24,
                $"the right edge drifted at row {y}: {decoded[target]} vs {aligned[source]}");
        }

        for (var x = 0; x < bufferWidth; x += 7)
        {
            var source = (((bufferHeight - 1) * alignedWidth) + x) * 4;
            var target = ((bufferHeight - 1) * stride) + (x * 4);
            Assert.True(
                Math.Abs(decoded[target + 1] - aligned[source + 1]) <= 24,
                $"the bottom edge drifted at column {x}: {decoded[target + 1]} vs {aligned[source + 1]}");
        }
    }

    [Fact]
    public void An_rgba_destination_is_swizzled_from_the_bgra_frame()
    {
        var decoder = RequireDecoder();
        const int width = 32, height = 16;
        var source = Gradient(width, height);
        var packet = VtEncoder.EncodeH264(source, width, height);
        Assert.SkipWhen(packet is null, "VideoToolbox on this host encodes no H.264");

        var bgra = new byte[width * height * 4];
        var rgba = new byte[width * height * 4];
        using (var session = decoder.Open(VideoCodec.H264, width, height, DrmFormat.Xrgb8888))
        using (var swizzled = decoder.Open(VideoCodec.H264, width, height, DrmFormat.Xbgr8888))
        {
            unsafe
            {
                fixed (byte* first = bgra)
                fixed (byte* second = rgba)
                {
                    Assert.True(session.Decode(packet!, (nint)first, width * 4));
                    Assert.True(swizzled.Decode(packet!, (nint)second, width * 4));
                }
            }
        }

        for (var i = 0; i < bgra.Length; i += 4)
        {
            Assert.Equal(bgra[i], rgba[i + 2]);
            Assert.Equal(bgra[i + 1], rgba[i + 1]);
            Assert.Equal(bgra[i + 2], rgba[i]);
        }
    }

    private static byte[]? EncodeWithX264(byte[] bgra, int width, int height)
    {
        var directory = Directory.CreateTempSubdirectory("basin-videotoolbox-test");
        try
        {
            var input = Path.Combine(directory.FullName, "in.raw");
            var output = Path.Combine(directory.FullName, "out.h264");
            File.WriteAllBytes(input, bgra);
            var info = new System.Diagnostics.ProcessStartInfo("ffmpeg")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[]
            {
                "-y", "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{width}x{height}", "-i", input,
                "-frames:v", "1", "-vf", "scale=out_color_matrix=bt601:out_range=tv,format=yuv420p",
                "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-qp", "4", "-f", "h264", output,
            })
            {
                info.ArgumentList.Add(argument);
            }

            using var encode = System.Diagnostics.Process.Start(info);
            if (encode is null)
            {
                return null;
            }

            _ = encode.StandardError.ReadToEnd();
            encode.WaitForExit(30_000);
            return encode.ExitCode == 0 ? File.ReadAllBytes(output) : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void An_x264_annex_b_stream_with_in_band_parameter_sets_decodes()
    {
        var decoder = RequireDecoder();
        const int width = 64, height = 64, stride = width * 4;
        var source = Gradient(width, height);
        var packet = EncodeWithX264(source, width, height);
        Assert.SkipWhen(packet is null, "no ffmpeg executable to encode the reference stream with");

        using var session = decoder.Open(VideoCodec.H264, width, height, DrmFormat.Xrgb8888);
        var decoded = new byte[height * stride];
        unsafe
        {
            fixed (byte* destination = decoded)
            {
                Assert.True(session.Decode(packet!, (nint)destination, stride));
                Assert.True(session.Decode(packet!, (nint)destination, stride));
            }
        }

        var sum = 0L;
        for (var i = 0; i < decoded.Length; i++)
        {
            var x = i / 4 % width;
            var y = i / 4 / width;
            if (i % 4 == 3 || x == 0 || y == 0 || x == width - 1 || y == height - 1)
            {
                continue;
            }

            sum += Math.Abs(decoded[i] - source[i]);
        }

        var mean = sum / (3L * (width - 2) * (height - 2));
        Assert.True(mean <= 4, $"the mean per-channel error is {mean}");
    }

    private static unsafe class VtEncoder
    {
        private const string VideoToolbox = "/System/Library/Frameworks/VideoToolbox.framework/VideoToolbox";
        private const string CoreMedia = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
        private const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        private static readonly object Gate = new();
        private static readonly List<byte[]> Encoded = [];

        internal static byte[]? EncodeH264(byte[] bgra, int width, int height)
        {
            nint session = 0;
            var status = VTCompressionSessionCreate(
                0, width, height, 0x61766331, 0, 0, 0,
                (nint)(delegate* unmanaged<nint, nint, int, uint, nint, void>)&OnEncoded, 0, &session);
            if (status != 0 || session == 0)
            {
                return null;
            }

            try
            {
                var trueValue = *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFBooleanTrue");
                var falseValue = *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFBooleanFalse");
                SetProperty(session, "RealTime", trueValue);
                SetProperty(session, "AllowFrameReordering", falseValue);
                var one = 1;
                var interval = CFNumberCreate(0, 3, &one);
                SetProperty(session, "MaxKeyFrameInterval", interval);
                CFRelease(interval);
                VTCompressionSessionPrepareToEncodeFrames(session);

                lock (Gate)
                {
                    Encoded.Clear();
                }

                {
                    nint pixelBuffer = 0;
                    status = CVPixelBufferCreate(0, (nuint)width, (nuint)height, 0x34323076, 0, &pixelBuffer);
                    if (status != 0 || pixelBuffer == 0)
                    {
                        return null;
                    }

                    FillLimitedRange601(pixelBuffer, bgra, width, height);
                    var time = new CMTime { Value = 0, Timescale = 60, Flags = 1 };
                    var duration = new CMTime { Value = 1, Timescale = 60, Flags = 1 };
                    uint info = 0;
                    status = VTCompressionSessionEncodeFrame(session, pixelBuffer, time, duration, 0, 0, &info);
                    var untilTime = new CMTime { Value = 0, Timescale = 0, Flags = 0 };
                    VTCompressionSessionCompleteFrames(session, untilTime);
                    CFRelease(pixelBuffer);
                    if (status != 0)
                    {
                        return null;
                    }
                }

                lock (Gate)
                {
                    return Encoded.Count == 0 ? null : Encoded[0];
                }
            }
            finally
            {
                VTCompressionSessionInvalidate(session);
                CFRelease(session);
            }
        }

        private static void FillLimitedRange601(nint pixelBuffer, byte[] bgra, int width, int height)
        {
            CVPixelBufferLockBaseAddress(pixelBuffer, 0);
            try
            {
                var luma = (byte*)CVPixelBufferGetBaseAddressOfPlane(pixelBuffer, 0);
                var lumaStride = (long)CVPixelBufferGetBytesPerRowOfPlane(pixelBuffer, 0);
                var chroma = (byte*)CVPixelBufferGetBaseAddressOfPlane(pixelBuffer, 1);
                var chromaStride = (long)CVPixelBufferGetBytesPerRowOfPlane(pixelBuffer, 1);
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var offset = ((y * width) + x) * 4;
                        int b = bgra[offset], g = bgra[offset + 1], r = bgra[offset + 2];
                        luma[(y * lumaStride) + x] = (byte)(((66 * r) + (129 * g) + (25 * b) + 128 >> 8) + 16);
                        if ((x & 1) == 0 && (y & 1) == 0)
                        {
                            var pair = chroma + ((y / 2) * chromaStride) + x;
                            pair[0] = (byte)(((-38 * r) - (74 * g) + (112 * b) + 128 >> 8) + 128);
                            pair[1] = (byte)(((112 * r) - (94 * g) - (18 * b) + 128 >> 8) + 128);
                        }
                    }
                }
            }
            finally
            {
                CVPixelBufferUnlockBaseAddress(pixelBuffer, 0);
            }
        }

        private static void SetProperty(nint session, string key, nint value)
        {
            var name = CreateString(key);
            VTSessionSetProperty(session, name, value);
            CFRelease(name);
        }

        private static nint CreateString(string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
            fixed (byte* cString = bytes)
            {
                return CFStringCreateWithCString(0, cString, 0x08000100);
            }
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly]
        private static void OnEncoded(nint refCon, nint sourceFrameRefCon, int status, uint infoFlags, nint sampleBuffer)
        {
            try
            {
                if (status != 0 || sampleBuffer == 0)
                {
                    return;
                }

                var description = CMSampleBufferGetFormatDescription(sampleBuffer);
                var stream = new List<byte>();
                for (nuint index = 0; index < 2; index++)
                {
                    byte* pointer = null;
                    nuint size = 0;
                    nuint count = 0;
                    var headerLength = 0;
                    if (CMVideoFormatDescriptionGetH264ParameterSetAtIndex(
                            description, index, &pointer, &size, &count, &headerLength) != 0)
                    {
                        return;
                    }

                    stream.AddRange([0, 0, 0, 1]);
                    stream.AddRange(new ReadOnlySpan<byte>(pointer, (int)size).ToArray());
                }

                var block = CMSampleBufferGetDataBuffer(sampleBuffer);
                var length = (int)CMBlockBufferGetDataLength(block);
                var avcc = new byte[length];
                fixed (byte* destination = avcc)
                {
                    if (CMBlockBufferCopyDataBytes(block, 0, (nuint)length, destination) != 0)
                    {
                        return;
                    }
                }

                var offset = 0;
                while (offset + 4 <= avcc.Length)
                {
                    var nalLength = (avcc[offset] << 24) | (avcc[offset + 1] << 16) | (avcc[offset + 2] << 8) | avcc[offset + 3];
                    offset += 4;
                    stream.AddRange([0, 0, 0, 1]);
                    stream.AddRange(avcc.AsSpan(offset, nalLength).ToArray());
                    offset += nalLength;
                }

                lock (Gate)
                {
                    Encoded.Add([.. stream]);
                }
            }
            catch (Exception)
            {
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CMTime
        {
            public long Value;

            public int Timescale;

            public uint Flags;

            public long Epoch;
        }

        [DllImport(VideoToolbox)]
        private static extern int VTCompressionSessionCreate(
            nint allocator, int width, int height, uint codecType, nint encoderSpecification,
            nint sourceImageBufferAttributes, nint compressedDataAllocator, nint outputCallback,
            nint outputCallbackRefCon, nint* compressionSessionOut);

        [DllImport(VideoToolbox)]
        private static extern int VTSessionSetProperty(nint session, nint propertyKey, nint propertyValue);

        [DllImport(VideoToolbox)]
        private static extern int VTCompressionSessionPrepareToEncodeFrames(nint session);

        [DllImport(VideoToolbox)]
        private static extern int VTCompressionSessionEncodeFrame(
            nint session, nint imageBuffer, CMTime presentationTimeStamp, CMTime duration,
            nint frameProperties, nint sourceFrameRefcon, uint* infoFlagsOut);

        [DllImport(VideoToolbox)]
        private static extern int VTCompressionSessionCompleteFrames(nint session, CMTime completeUntilPresentationTimeStamp);

        [DllImport(VideoToolbox)]
        private static extern void VTCompressionSessionInvalidate(nint session);

        [DllImport(CoreVideo)]
        private static extern int CVPixelBufferCreate(
            nint allocator, nuint width, nuint height, uint pixelFormatType, nint pixelBufferAttributes, nint* pixelBufferOut);

        [DllImport(CoreVideo)]
        private static extern int CVPixelBufferLockBaseAddress(nint pixelBuffer, ulong lockFlags);

        [DllImport(CoreVideo)]
        private static extern int CVPixelBufferUnlockBaseAddress(nint pixelBuffer, ulong unlockFlags);

        [DllImport(CoreVideo)]
        private static extern void* CVPixelBufferGetBaseAddressOfPlane(nint pixelBuffer, nuint planeIndex);

        [DllImport(CoreVideo)]
        private static extern nuint CVPixelBufferGetBytesPerRowOfPlane(nint pixelBuffer, nuint planeIndex);

        [DllImport(CoreMedia)]
        private static extern nint CMSampleBufferGetFormatDescription(nint sampleBuffer);

        [DllImport(CoreMedia)]
        private static extern nint CMSampleBufferGetDataBuffer(nint sampleBuffer);

        [DllImport(CoreMedia)]
        private static extern int CMVideoFormatDescriptionGetH264ParameterSetAtIndex(
            nint description, nuint index, byte** parameterSetPointerOut, nuint* parameterSetSizeOut,
            nuint* parameterSetCountOut, int* nalUnitHeaderLengthOut);

        [DllImport(CoreMedia)]
        private static extern nuint CMBlockBufferGetDataLength(nint blockBuffer);

        [DllImport(CoreMedia)]
        private static extern int CMBlockBufferCopyDataBytes(nint blockBuffer, nuint offset, nuint length, void* destination);

        [DllImport(CoreFoundation)]
        private static extern nint CFStringCreateWithCString(nint allocator, byte* cString, uint encoding);

        [DllImport(CoreFoundation)]
        private static extern nint CFNumberCreate(nint allocator, nint type, void* valuePointer);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(nint reference);
    }
}
