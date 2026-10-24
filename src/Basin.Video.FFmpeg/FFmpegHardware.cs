using Basin.Diagnostics;
using static Basin.Video.FFmpeg.FFmpegLog;

namespace Basin.Video.FFmpeg;

internal sealed unsafe class FFmpegHardware : IDisposable
{
    private nint _device;

    private FFmpegHardware(nint device, int pixelFormat, string name)
    {
        _device = device;
        PixelFormat = pixelFormat;
        Name = name;
        FFmpegCensus.Track();
    }

    internal nint Device => _device;

    internal int PixelFormat { get; }

    internal string Name { get; }

    internal static FFmpegHardware? TryCreate()
    {
        (string Type, string Format)[] candidates = OperatingSystem.IsWindows()
            ? [("d3d11va", "d3d11")]
            : OperatingSystem.IsMacOS()
                ? [("videotoolbox", "videotoolbox_vld")]
                : [("vaapi", "vaapi"), ("cuda", "cuda")];

        foreach (var (typeName, formatName) in candidates)
        {
            var type = TypeByName(typeName);
            if (type <= 0)
            {
                continue;
            }

            var pixelFormat = FFmpegNative.PixelFormat(formatName);
            if (pixelFormat < 0)
            {
                continue;
            }

            nint device = 0;
            var made = FFmpegNative.av_hwdevice_ctx_create(&device, type, null, 0, 0);
            if (made < 0)
            {
                Log.Debug($"no {typeName} device opens here: {FFmpegNative.DescribeError(made)}");
                continue;
            }

            return new FFmpegHardware(device, pixelFormat, typeName);
        }

        return null;
    }

    public void Dispose()
    {
        if (_device == 0)
        {
            return;
        }

        var device = _device;
        _device = 0;
        FFmpegNative.av_buffer_unref(&device);
        FFmpegCensus.Untrack();
    }

    private static int TypeByName(string name)
    {
        Span<byte> bytes = stackalloc byte[32];
        var written = System.Text.Encoding.ASCII.GetBytes(name, bytes[..^1]);
        bytes[written] = 0;
        fixed (byte* text = bytes)
        {
            return FFmpegNative.av_hwdevice_find_type_by_name(text);
        }
    }
}
