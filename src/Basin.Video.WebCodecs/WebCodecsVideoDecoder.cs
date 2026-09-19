using Basin.Capabilities;
using static Basin.Video.WebCodecs.WebCodecsLog;

namespace Basin.Video.WebCodecs;

public sealed class WebCodecsVideoDecoder : IVideoDecoder
{
    private readonly bool _h264;
    private readonly bool _vp9;
    private readonly bool _av1;

    private WebCodecsVideoDecoder(bool h264, bool vp9, bool av1)
    {
        _h264 = h264;
        _vp9 = vp9;
        _av1 = av1;
    }

    public static string? WhyNot { get; private set; }

    public static async Task<WebCodecsVideoDecoder?> TryCreateAsync()
    {
        if (!OperatingSystem.IsBrowser())
        {
            WhyNot = "WebCodecs exists only in a browser";
            return null;
        }

        try
        {
            await WebCodecsInterop.ImportAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            WhyNot = $"the WebCodecs module did not import: {error.Message}";
            return null;
        }

        if (!WebCodecsInterop.Available())
        {
            WhyNot = "this browser has no WebCodecs VideoDecoder";
            return null;
        }

        var h264 = await WebCodecsInterop.Supports(CodecString(VideoCodec.H264)).ConfigureAwait(true);
        var vp9 = await WebCodecsInterop.Supports(CodecString(VideoCodec.Vp9)).ConfigureAwait(true);
        var av1 = await WebCodecsInterop.Supports(CodecString(VideoCodec.Av1)).ConfigureAwait(true);
        WhyNot = null;
        Log.Debug($"decode over WebCodecs: h264 {h264}, vp9 {vp9}, av1 {av1}");
        return new WebCodecsVideoDecoder(h264, vp9, av1);
    }

    public bool Supports(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => _h264,
        VideoCodec.Vp9 => _vp9,
        VideoCodec.Av1 => _av1,
        _ => false,
    };

    public IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format)
    {
        if (!Supports(codec))
        {
            throw new NotSupportedException($"this browser's WebCodecs decodes no {codec}");
        }

        return new WebCodecsDecodeSession(codec, CodecString(codec), width, height, format);
    }

    internal static string CodecString(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "avc1.640028",
        VideoCodec.Vp9 => "vp09.00.10.08",
        VideoCodec.Av1 => "av01.0.08M.08",
        _ => throw new ArgumentOutOfRangeException(nameof(codec)),
    };
}
