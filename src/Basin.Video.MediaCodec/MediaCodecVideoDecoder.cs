using Basin.Capabilities;

namespace Basin.Video.MediaCodec;

public sealed class MediaCodecVideoDecoder : IVideoDecoder
{
    private MediaCodecVideoDecoder()
    {
    }

    public static MediaCodecVideoDecoder? TryCreate(out string? whyNot) =>
        MediaCodecNative.TryLoad(out whyNot) ? new MediaCodecVideoDecoder() : null;

    public static MediaCodecVideoDecoder? TryCreate() => TryCreate(out _);

    public bool Supports(VideoCodec codec)
    {
        var probe = MediaCodecNative.CreateDecoder(MimeOf(codec));
        if (probe == 0)
        {
            return false;
        }

        MediaCodecNative.AMediaCodec_delete(probe);
        return true;
    }

    public IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format) =>
        new MediaCodecDecodeSession(MimeOf(codec), width, height, format);

    internal static string MimeOf(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "video/avc",
        VideoCodec.Vp9 => "video/x-vnd.on2.vp9",
        VideoCodec.Av1 => "video/av01",
        _ => throw new ArgumentOutOfRangeException(nameof(codec)),
    };
}
