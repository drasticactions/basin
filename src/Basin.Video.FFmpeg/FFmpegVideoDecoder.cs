using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Video.FFmpeg;

public sealed class FFmpegVideoDecoder : IVideoDecoder
{
    private readonly FFmpegHardware? _hardware;

    private FFmpegVideoDecoder(FFmpegHardware? hardware) => _hardware = hardware;

    public static FFmpegVideoDecoder? TryCreate(out string? whyNot) => TryCreate(preferHardware: false, out whyNot);

    public static FFmpegVideoDecoder? TryCreate(bool preferHardware, out string? whyNot)
    {
        if (!FFmpegNative.TryLoad(out whyNot))
        {
            return null;
        }

        FFmpegHardware? hardware = null;
        if (preferHardware)
        {
            hardware = FFmpegHardware.TryCreate();
            if (hardware is null)
            {
                BasinLog.Warn($"ffmpeg: no hardware decode device opens on this host; decoding in software");
            }
            else
            {
                FFmpegNative.HardwarePixelFormat = hardware.PixelFormat;
                BasinLog.Debug($"ffmpeg: hardware decode over {hardware.Name}");
            }
        }

        return new FFmpegVideoDecoder(hardware);
    }

    public static FFmpegVideoDecoder? TryCreate() => TryCreate(out _);

    public string? HardwareName => _hardware?.Name;

    public bool Supports(VideoCodec codec)
    {
        var id = FFmpegNative.CodecId(CodecName(codec));
        if (id < 0)
        {
            return false;
        }

        unsafe
        {
            return FFmpegNative.avcodec_find_decoder(id) != 0;
        }
    }

    public IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format) =>
        new FFmpegDecodeSession(CodecName(codec), width, height, format, _hardware);

    internal static string CodecName(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264",
        VideoCodec.Vp9 => "vp9",
        VideoCodec.Av1 => "av1",
        _ => throw new ArgumentOutOfRangeException(nameof(codec)),
    };
}
