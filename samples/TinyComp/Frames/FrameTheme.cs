using System.Reflection;
using Basin;
using Basin.Freedesktop;
using Basin.Icons.Skia;
using Basin.Render.Skia;
using Basin.Capabilities;
using Basin.UI.Skia;
using SkiaSharp;

namespace TinyComp;

internal sealed class FrameTheme : IDisposable
{
    private const int IconSide = 18;

    private readonly Dictionary<int, IconSearch> _searches = [];
    private readonly Dictionary<(string Name, int Scale), SKImage?> _icons = [];
    private bool _disposed;

    public FrameTheme(float fontSize)
    {
        FontSize = fontSize;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NotoSansCJK-Regular.ttc")
            ?? throw new InvalidOperationException("The embedded frame font is missing.");
        using var data = SKData.Create(stream);
        Typeface = SkiaTypefaces.FromCollection(data, "Noto Sans CJK JP")
            ?? throw new InvalidOperationException("The embedded font has no 'Noto Sans CJK JP' face.");
        TitleFont = SkiaCensus.Track(new SKFont(Typeface, fontSize) { Subpixel = true });
        BadgeFont = SkiaCensus.Track(new SKFont(Typeface, MathF.Round(fontSize * 11 / 14)) { Subpixel = true });
        TabFont = SkiaCensus.Track(new SKFont(Typeface, MathF.Round(fontSize * 12 / 14)) { Subpixel = true, Embolden = true });
        Text = new SkiaShapedTextCache(Typeface);
        Badges = new SkiaShapedTextCache(Typeface);
        TabText = new SkiaShapedTextCache(Typeface);
        Fill = SkiaCensus.Track(new SKPaint { IsAntialias = true });
        Stroke = SkiaCensus.Track(new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        });
        Hairline = SkiaCensus.Track(new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            StrokeCap = SKStrokeCap.Butt,
        });
    }

    public float FontSize { get; }

    public SKTypeface Typeface { get; }

    public SKFont TitleFont { get; }

    public SKFont BadgeFont { get; }

    public SKFont TabFont { get; }

    public SkiaShapedTextCache Text { get; }

    public SkiaShapedTextCache Badges { get; }

    public SkiaShapedTextCache TabText { get; }

    public SKPaint Fill { get; }

    public SKPaint Stroke { get; }

    public SKPaint Hairline { get; }

    private readonly Dictionary<IBuffer, SKImage?> _pixelIcons = [];

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

    public SKImage? IconFor(string name, int scale)
    {
        scale = Math.Max(1, scale);
        if (_icons.TryGetValue((name, scale), out var cached))
        {
            return cached;
        }

        if (!_searches.TryGetValue(scale, out var search))
        {
            search = new IconSearch { Sizes = [48, 32], Scale = scale };
            _searches[scale] = search;
        }

        var image = search.Find(name) is { } path ? IconRasterizer.Image(path, IconSide * scale) : null;
        _icons[(name, scale)] = image;
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
        Badges.Dispose();
        TabText.Dispose();
        SkiaCensus.Release(Fill);
        SkiaCensus.Release(Stroke);
        SkiaCensus.Release(Hairline);
        SkiaCensus.Release(TitleFont);
        SkiaCensus.Release(BadgeFont);
        SkiaCensus.Release(TabFont);
        SkiaCensus.Release(Typeface);
    }
}
