using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Video.FFmpeg.FFmpegLog;

namespace Basin.Video.FFmpeg;

/// <summary>
/// Decodes video through the system FFmpeg libraries, loaded at run time by soname. It can decode on a hardware device when one opens.
/// </summary>
public sealed class FFmpegVideoDecoder : IVideoDecoder
{
    private readonly FFmpegHardware? _hardware;

    private FFmpegVideoDecoder(FFmpegHardware? hardware) => _hardware = hardware;

    /// <summary>Creates a software decoder when libavcodec, libavutil and libswscale load.</summary>
    /// <param name="whyNot">The reason for a <see langword="null"/> result, or <see langword="null"/> on success.</param>
    /// <returns>The decoder, or <see langword="null"/> when a library does not load.</returns>
    public static FFmpegVideoDecoder? TryCreate(out string? whyNot) => TryCreate(preferHardware: false, out whyNot);

    /// <summary>
    /// Creates a decoder when the FFmpeg libraries load. With <paramref name="preferHardware"/>, it tries VAAPI, CUDA, D3D11VA or VideoToolbox, and falls back to software.
    /// </summary>
    /// <param name="preferHardware"><see langword="true"/> to try a hardware decode device.</param>
    /// <param name="whyNot">The reason for a <see langword="null"/> result, or <see langword="null"/> on success.</param>
    /// <returns>The decoder, or <see langword="null"/> when a library does not load.</returns>
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
                Log.Warn($"no hardware decode device opens on this host; decoding in software");
            }
            else
            {
                FFmpegNative.HardwarePixelFormat = hardware.PixelFormat;
                Log.Debug($"hardware decode over {hardware.Name}");
            }
        }

        return new FFmpegVideoDecoder(hardware);
    }

    /// <summary>Creates a software decoder when the FFmpeg libraries load.</summary>
    /// <returns>The decoder, or <see langword="null"/> when a library does not load.</returns>
    public static FFmpegVideoDecoder? TryCreate() => TryCreate(out _);

    /// <summary>Gets the hardware device type that sessions decode on, for example <c>vaapi</c>, or <see langword="null"/> for software.</summary>
    public string? HardwareName => _hardware?.Name;

    /// <summary>Returns whether the system FFmpeg has a decoder for <paramref name="codec"/>.</summary>
    /// <param name="codec">The codec to check.</param>
    /// <returns><see langword="true"/> when libavcodec finds a decoder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="codec"/> is not a known codec.</exception>
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

    /// <summary>Opens a session that decodes one stream into buffers of one size and format.</summary>
    /// <param name="codec">The codec of the stream.</param>
    /// <param name="width">The destination width in pixels. Larger decoded frames are cropped.</param>
    /// <param name="height">The destination height in pixels. Larger decoded frames are cropped.</param>
    /// <param name="format">The destination pixel format.</param>
    /// <returns>The session. The caller disposes it.</returns>
    /// <exception cref="NotSupportedException">FFmpeg has no decoder for <paramref name="codec"/> or swscale cannot write <paramref name="format"/>.</exception>
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
