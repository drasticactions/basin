namespace Basin.Capabilities;

public interface IVideoDecoder
{
    bool Supports(VideoCodec codec);

    IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format);
}
