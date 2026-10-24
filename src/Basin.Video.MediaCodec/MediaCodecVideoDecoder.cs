using Basin.Capabilities;

namespace Basin.Video.MediaCodec;

/// <summary>
/// Decodes video through Android's <c>AMediaCodec</c> from <c>libmediandk.so</c>, into buffers that this process reads.
/// </summary>
public sealed class MediaCodecVideoDecoder : IVideoDecoder
{
    private MediaCodecVideoDecoder()
    {
    }

    /// <summary>Creates a decoder when <c>libmediandk.so</c> loads.</summary>
    /// <param name="whyNot">The reason for a <see langword="null"/> result, or <see langword="null"/> on success.</param>
    /// <returns>The decoder, or <see langword="null"/> when the library does not load.</returns>
    public static MediaCodecVideoDecoder? TryCreate(out string? whyNot) =>
        MediaCodecNative.TryLoad(out whyNot) ? new MediaCodecVideoDecoder() : null;

    /// <summary>Creates a decoder when <c>libmediandk.so</c> loads.</summary>
    /// <returns>The decoder, or <see langword="null"/> when the library does not load.</returns>
    public static MediaCodecVideoDecoder? TryCreate() => TryCreate(out _);

    /// <summary>Returns whether the device creates a decoder for the MIME type of <paramref name="codec"/>. It creates one and deletes it to check.</summary>
    /// <param name="codec">The codec to check.</param>
    /// <returns><see langword="true"/> when the device creates a decoder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="codec"/> is not a known codec.</exception>
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

    /// <summary>Opens a session that decodes one stream into buffers of one size and format.</summary>
    /// <param name="codec">The codec of the stream.</param>
    /// <param name="width">The destination width in pixels. Larger decoded frames are cropped.</param>
    /// <param name="height">The destination height in pixels. Larger decoded frames are cropped.</param>
    /// <param name="format">The destination pixel format.</param>
    /// <returns>The session. The caller disposes it.</returns>
    /// <exception cref="NotSupportedException">The device has no decoder for <paramref name="codec"/> or the session cannot write <paramref name="format"/>.</exception>
    /// <exception cref="InvalidOperationException">The codec does not configure.</exception>
    public IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format) =>
        new MediaCodecDecodeSession(MimeOf(codec), width, height, format);

    internal static string MimeOf(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "video/avc",
        VideoCodec.Vp9 => "video/x-vnd.on2.vp9",
        VideoCodec.Av1 => "video/av01",
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "The codec is not supported."),
    };
}
