using SkiaSharp;

namespace RetroWm;

internal sealed class RetroIconLoader
{
    private static readonly int[,] Bayer = new int[4, 4]
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 },
    };

    private const int DitherSpread = 64;

    private readonly Basin.WindowManager.Skia.IconRaster _loader = new(new Basin.Cli.IconSearch
    {
        OverrideDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "retro-wm", "icons"),
    }.Find);
    private readonly Dictionary<string, SKImage?> _cache = [];

    public SKImage? Load(string appId, int scale)
    {
        if (!Theme.IconDither)
        {
            return _loader.Load(appId, Theme.IconSize * Math.Max(scale, 1));
        }

        if (_cache.TryGetValue(appId, out var cached))
        {
            return cached;
        }

        var source = _loader.Load(appId, Theme.IconSize);
        var retro = source is null ? null : Quantize(source);
        _cache[appId] = retro;
        return retro;
    }

    public void Clear() => _cache.Clear();

    private static SKImage? Quantize(SKImage source)
    {
        var size = Theme.IconSize;
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(source, new SKRect(0, 0, size, size), new SKSamplingOptions(SKFilterMode.Linear));
        }

        var pixels = bitmap.GetPixelSpan();
        var output = new SKBitmap(info);
        var target = output.GetPixels();
        unsafe
        {
            var dst = new Span<byte>((void*)target, pixels.Length);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var offset = ((y * size) + x) * 4;
                    var alpha = pixels[offset + 3];
                    if (alpha < 128)
                    {
                        dst[offset] = 0;
                        dst[offset + 1] = 0;
                        dst[offset + 2] = 0;
                        dst[offset + 3] = 0;
                        continue;
                    }

                    var bias = (int)Math.Round(((Bayer[y & 3, x & 3] / 16.0) - 0.5) * DitherSpread);
                    var b = Math.Clamp(pixels[offset] + bias, 0, 255);
                    var g = Math.Clamp(pixels[offset + 1] + bias, 0, 255);
                    var r = Math.Clamp(pixels[offset + 2] + bias, 0, 255);
                    var nearest = NearestEga(r, g, b);
                    dst[offset] = (byte)(nearest >> 8);
                    dst[offset + 1] = (byte)(nearest >> 16);
                    dst[offset + 2] = (byte)(nearest >> 24);
                    dst[offset + 3] = 255;
                }
            }
        }

        var image = SKImage.FromBitmap(output);
        output.Dispose();
        return image;
    }

    private static uint NearestEga(int r, int g, int b)
    {
        var best = Ega.Black;
        var bestDistance = long.MaxValue;
        foreach (var candidate in Ega.Palette)
        {
            var cr = (int)((candidate >> 24) & 0xFF);
            var cg = (int)((candidate >> 16) & 0xFF);
            var cb = (int)((candidate >> 8) & 0xFF);
            var distance = ((long)(r - cr) * (r - cr))
                + ((long)(g - cg) * (g - cg))
                + ((long)(b - cb) * (b - cb));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }
}
