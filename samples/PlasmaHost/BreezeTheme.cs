using System.Reflection;
using Basin;
using Basin.Render.Skia;
using Basin.UI.Skia;
using SkiaSharp;

namespace PlasmaHost;

internal sealed class BreezeTheme : IDisposable
{
    private readonly Dictionary<string, BreezePalette> _palettes = [];
    private readonly Dictionary<string, SKImage?> _icons = [];
    private readonly Dictionary<IBuffer, SKImage?> _pixelIcons = [];
    private bool _disposed;

    public BreezeTheme()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NotoSansCJK-Regular.ttc")
            ?? throw new InvalidOperationException("embedded frame font missing");
        using var data = SKData.Create(stream);
        Typeface = SkiaTypefaces.FromCollection(data, "Noto Sans CJK JP")
            ?? throw new InvalidOperationException("embedded font has no 'Noto Sans CJK JP' face");
        TitleFont = SkiaCensus.Track(new SKFont(Typeface, 13) { Subpixel = true });
        Text = new SkiaShapedTextCache(Typeface);
        Fill = SkiaCensus.Track(new SKPaint { IsAntialias = true });
        Stroke = SkiaCensus.Track(new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        });
        Config = KdeDecorationConfig.Load();
    }

    public SKTypeface Typeface { get; }

    public SKFont TitleFont { get; }

    public SkiaShapedTextCache Text { get; }

    public SKPaint Fill { get; }

    public SKPaint Stroke { get; }

    public KdeDecorationConfig Config { get; }

    public BreezePalette PaletteFor(string? palette)
    {
        var key = palette ?? "";
        if (!_palettes.TryGetValue(key, out var colors))
        {
            colors = BreezePalette.Load(palette);
            _palettes[key] = colors;
        }

        return colors;
    }

    public SKImage? ImageFor(IBuffer buffer)
    {
        if (_pixelIcons.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        SKImage? image = null;
        if (SkiaRenderer.TryImageInfo(buffer.Width, buffer.Height, DrmFormat.Argb8888, out var info) &&
            buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            var copied = SKImage.FromPixelCopy(info, view.Data, view.Stride);
            buffer.EndDataAccess();
            if (copied is not null)
            {
                image = SkiaCensus.Track(copied);
            }
        }

        _pixelIcons[buffer] = image;
        buffer.Destroyed += () =>
        {
            if (_pixelIcons.Remove(buffer, out var dead) && dead is not null)
            {
                SkiaCensus.Release(dead);
            }
        };
        return image;
    }

    public SKImage? IconFor(string name)
    {
        if (_icons.TryGetValue(name, out var cached))
        {
            return cached;
        }

        SKImage? image = null;
        foreach (var path in (ReadOnlySpan<string>)
        [
            $"/usr/share/icons/hicolor/48x48/apps/{name}.png",
            $"/usr/share/icons/hicolor/32x32/apps/{name}.png",
            $"/usr/share/pixmaps/{name}.png",
        ])
        {
            if (!File.Exists(path))
            {
                continue;
            }

            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is not null)
            {
                var decoded = SKImage.FromBitmap(bitmap);
                if (decoded is not null)
                {
                    image = SkiaCensus.Track(decoded);
                }
            }

            break;
        }

        _icons[name] = image;
        return image;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var icon in _icons.Values)
        {
            if (icon is not null)
            {
                SkiaCensus.Release(icon);
            }
        }

        _icons.Clear();
        foreach (var icon in _pixelIcons.Values)
        {
            if (icon is not null)
            {
                SkiaCensus.Release(icon);
            }
        }

        _pixelIcons.Clear();
        Text.Dispose();
        SkiaCensus.Release(Fill);
        SkiaCensus.Release(Stroke);
        SkiaCensus.Release(TitleFont);
        SkiaCensus.Release(Typeface);
    }
}
