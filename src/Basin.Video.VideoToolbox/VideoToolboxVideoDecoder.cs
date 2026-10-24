using Basin.Capabilities;

namespace Basin.Video.VideoToolbox;

/// <summary>
/// Decodes video through Apple's <c>VTDecompressionSession</c> into BGRA, then packs it into the destination format.
/// </summary>
public sealed class VideoToolboxVideoDecoder : IVideoDecoder
{
    private VideoToolboxVideoDecoder()
    {
    }

    /// <summary>Creates a decoder when the VideoToolbox, CoreMedia, CoreVideo and CoreFoundation frameworks load.</summary>
    /// <param name="whyNot">The reason for a <see langword="null"/> result, or <see langword="null"/> on success.</param>
    /// <returns>The decoder, or <see langword="null"/> when a framework does not load.</returns>
    public static VideoToolboxVideoDecoder? TryCreate(out string? whyNot) =>
        VideoToolboxNative.TryLoad(out whyNot) ? new VideoToolboxVideoDecoder() : null;

    /// <summary>Creates a decoder when the Apple frameworks load.</summary>
    /// <returns>The decoder, or <see langword="null"/> when a framework does not load.</returns>
    public static VideoToolboxVideoDecoder? TryCreate() => TryCreate(out _);

    /// <summary>
    /// Returns whether this host decodes <paramref name="codec"/>. H.264 is always supported. VP9 and AV1 need hardware decode, and a VP9 check registers the supplemental decoder.
    /// </summary>
    /// <param name="codec">The codec to check.</param>
    /// <returns><see langword="true"/> when a session can decode the codec.</returns>
    public bool Supports(VideoCodec codec)
    {
        switch (codec)
        {
            case VideoCodec.H264:
                return true;
            case VideoCodec.Vp9:
                VideoToolboxNative.RegisterSupplementalDecoder(VideoToolboxNative.CodecTypeVp9);
                return VideoToolboxNative.IsHardwareDecodeSupported(VideoToolboxNative.CodecTypeVp9);
            case VideoCodec.Av1:
                return VideoToolboxNative.IsHardwareDecodeSupported(VideoToolboxNative.CodecTypeAv1);
            default:
                return false;
        }
    }

    /// <summary>Opens a session that decodes one stream into buffers of one size and format.</summary>
    /// <param name="codec">The codec of the stream.</param>
    /// <param name="width">The destination width in pixels. Larger decoded frames are cropped.</param>
    /// <param name="height">The destination height in pixels. Larger decoded frames are cropped.</param>
    /// <param name="format">The destination pixel format.</param>
    /// <returns>The session. The caller disposes it.</returns>
    /// <exception cref="NotSupportedException"><see cref="Supports"/> returns <see langword="false"/> for <paramref name="codec"/>, or the session cannot write <paramref name="format"/>.</exception>
    public IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format)
    {
        if (!Supports(codec))
        {
            throw new NotSupportedException($"VideoToolbox on this host does not decode {codec}.");
        }

        return new VideoToolboxDecodeSession(codec, width, height, format);
    }
}
