using Basin.Capabilities;
using static Basin.Video.WebCodecs.WebCodecsLog;

namespace Basin.Video.WebCodecs;

/// <summary>
/// Decodes video through the browser's WebCodecs <c>VideoDecoder</c>. Its sessions are asynchronous and implement <see cref="IAsyncVideoDecodeSession"/>.
/// </summary>
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

    /// <summary>
    /// Gets the reason that the last <see cref="TryCreateAsync"/> returned <see langword="null"/>, or <see langword="null"/> after a success.
    /// </summary>
    public static string? WhyNot { get; private set; }

    /// <summary>
    /// Imports the WebCodecs module and asks the browser once which codecs it decodes. <see cref="Supports"/> answers from that probe.
    /// </summary>
    /// <returns>The decoder, or <see langword="null"/> outside a browser, when the module does not import, or without <c>VideoDecoder</c>. <see cref="WhyNot"/> holds the reason.</returns>
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

    /// <summary>Returns the answer that the browser gave for <paramref name="codec"/> when the decoder was created.</summary>
    /// <param name="codec">The codec to check.</param>
    /// <returns><see langword="true"/> when the browser accepts the codec configuration.</returns>
    public bool Supports(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => _h264,
        VideoCodec.Vp9 => _vp9,
        VideoCodec.Av1 => _av1,
        _ => false,
    };

    /// <summary>Opens an asynchronous session that decodes one stream into buffers of one size and format.</summary>
    /// <param name="codec">The codec of the stream.</param>
    /// <param name="width">The destination width in pixels. Larger decoded frames are cropped.</param>
    /// <param name="height">The destination height in pixels. Larger decoded frames are cropped.</param>
    /// <param name="format">The destination pixel format.</param>
    /// <returns>The session, which also implements <see cref="IAsyncVideoDecodeSession"/>. The caller disposes it.</returns>
    /// <exception cref="NotSupportedException"><see cref="Supports"/> returns <see langword="false"/> for <paramref name="codec"/>, or the session cannot write <paramref name="format"/>.</exception>
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
