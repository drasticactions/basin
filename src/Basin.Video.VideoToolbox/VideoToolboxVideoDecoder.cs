using Basin.Capabilities;

namespace Basin.Video.VideoToolbox;

public sealed class VideoToolboxVideoDecoder : IVideoDecoder
{
    private VideoToolboxVideoDecoder()
    {
    }

    public static VideoToolboxVideoDecoder? TryCreate(out string? whyNot) =>
        VideoToolboxNative.TryLoad(out whyNot) ? new VideoToolboxVideoDecoder() : null;

    public static VideoToolboxVideoDecoder? TryCreate() => TryCreate(out _);

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

    public IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format)
    {
        if (!Supports(codec))
        {
            throw new NotSupportedException($"VideoToolbox on this host decodes no {codec}");
        }

        return new VideoToolboxDecodeSession(codec, width, height, format);
    }
}
