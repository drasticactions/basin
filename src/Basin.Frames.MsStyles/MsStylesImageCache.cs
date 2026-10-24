using Basin.Themes.MsStyles;
using SkiaSharp;

namespace Basin.Frames.MsStyles;

/// <summary>
/// Decodes each theme image once into a premultiplied BGRA <see cref="MsStylesBitmap"/>, and once more for each transparency key. It holds managed arrays only.
/// </summary>
public sealed class MsStylesImageCache
{
    /// <summary>
    /// The key value that asks for the image with no transparency key.
    /// </summary>
    public const int NoKey = -1;

    /// <summary>
    /// The default transparency key, magenta, as a COLORREF (0x00BBGGRR).
    /// </summary>
    public const int DefaultKey = 0xff00ff;

    private readonly Dictionary<(int Image, int Key), MsStylesBitmap?> _byImage = [];
    private readonly List<MsStylesBitmap> _bitmaps = [];

    /// <summary>
    /// Gets the number of bitmaps in the cache. Handles run from 0 to this number less 1.
    /// </summary>
    public int Count => _bitmaps.Count;

    /// <summary>
    /// Gets the number of images that failed to decode. Each image counts once.
    /// </summary>
    public int DecodeFailures { get; private set; }

    /// <summary>
    /// Gets a bitmap by its <see cref="MsStylesBitmap.Handle"/>.
    /// </summary>
    /// <param name="handle">The handle that a draw operation carries.</param>
    public MsStylesBitmap this[int handle] => _bitmaps[handle];

    /// <summary>
    /// Gets the decoded bitmap for a theme image, and decodes it on the first request. The result, including a failure, is cached.
    /// </summary>
    /// <param name="image">The theme image.</param>
    /// <param name="keyColor">The transparency key as a COLORREF, or <see cref="NoKey"/>. A key has no effect on an image with alpha.</param>
    /// <returns>The bitmap, or <see langword="null"/> when the image cannot be decoded.</returns>
    public MsStylesBitmap? Get(in MsStylesImage image, int keyColor = NoKey)
    {
        if (_byImage.TryGetValue((image.Id, keyColor), out var cached))
        {
            return cached;
        }

        var decoded = keyColor == NoKey ? Decode(image) : Keyed(image, keyColor);
        _byImage[(image.Id, keyColor)] = decoded;
        return decoded;
    }

    /// <summary>
    /// Premultiplies BGRA pixels in place, with rounding.
    /// </summary>
    /// <param name="bgra">The pixels, four bytes each, with straight alpha.</param>
    public static void Premultiply(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255)
            {
                continue;
            }

            bgra[i] = (byte)((bgra[i] * a + 127) / 255);
            bgra[i + 1] = (byte)((bgra[i + 1] * a + 127) / 255);
            bgra[i + 2] = (byte)((bgra[i + 2] * a + 127) / 255);
        }
    }

    private MsStylesBitmap? Keyed(in MsStylesImage image, int keyColor)
    {
        if (Get(image) is not { } source)
        {
            return null;
        }

        if (source.HasAlpha || (keyColor == DefaultKey && !source.HasDefaultKey))
        {
            return source;
        }

        var pixels = (byte[])source.Pixels.Clone();
        var b = (byte)(keyColor >> 16);
        var g = (byte)(keyColor >> 8);
        var r = (byte)keyColor;
        var any = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] == b && pixels[i + 1] == g && pixels[i + 2] == r)
            {
                pixels[i] = 0;
                pixels[i + 1] = 0;
                pixels[i + 2] = 0;
                pixels[i + 3] = 0;
                any = true;
            }
        }

        if (!any)
        {
            return source;
        }

        return Add(source.Width, source.Height, pixels, hasAlpha: true, hasDefaultKey: source.HasDefaultKey);
    }

    private MsStylesBitmap? Decode(in MsStylesImage image)
    {
        var bytes = image.Bytes.Span;
        if (image.Format == MsStylesImageFormat.Dib || (bytes.Length > 2 && bytes[0] == 'B' && bytes[1] == 'M'))
        {
            if (MsStylesDib.TryDecode(bytes, out var width, out var height, out var pixels, out var hasAlpha, out var hasDefaultKey))
            {
                return Add(width, height, pixels, hasAlpha, hasDefaultKey);
            }

            DecodeFailures++;
            return null;
        }

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            DecodeFailures++;
            return null;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var decoded = new byte[info.BytesSize];
        unsafe
        {
            fixed (byte* target = decoded)
            {
                var result = codec.GetPixels(info, (nint)target);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                {
                    DecodeFailures++;
                    return null;
                }
            }
        }

        var alpha = false;
        var key = false;
        for (var i = 0; i < decoded.Length; i += 4)
        {
            if (decoded[i + 3] != 255)
            {
                alpha = true;
            }
            else if (decoded[i] == 0xff && decoded[i + 1] == 0 && decoded[i + 2] == 0xff)
            {
                key = true;
            }
        }

        if (!image.Premultiplied)
        {
            Premultiply(decoded);
        }

        return Add(info.Width, info.Height, decoded, alpha, key);
    }

    private MsStylesBitmap Add(int width, int height, byte[] pixels, bool hasAlpha, bool hasDefaultKey)
    {
        var bitmap = new MsStylesBitmap(_bitmaps.Count, width, height, pixels, hasAlpha, hasDefaultKey);
        _bitmaps.Add(bitmap);
        return bitmap;
    }
}
