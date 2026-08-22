using System.Reflection;
using Basin;
using Basin.Render.Skia;
using Basin.Capabilities;
using Basin.UI.Skia;
using SkiaSharp;

namespace TinyComp;

internal sealed class FrameTheme : IDisposable
{
    private readonly Dictionary<string, SKImage?> _icons = [];
    private bool _disposed;

    public FrameTheme()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NotoSansCJK-Regular.ttc")
            ?? throw new InvalidOperationException("embedded frame font missing");
        using var data = SKData.Create(stream);
        Typeface = SkiaTypefaces.FromCollection(data, "Noto Sans CJK JP")
            ?? throw new InvalidOperationException("embedded font has no 'Noto Sans CJK JP' face");
        TitleFont = SkiaCensus.Track(new SKFont(Typeface, 14) { Subpixel = true });
        BadgeFont = SkiaCensus.Track(new SKFont(Typeface, 11) { Subpixel = true });
        TabFont = SkiaCensus.Track(new SKFont(Typeface, 12) { Subpixel = true, Embolden = true });
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
