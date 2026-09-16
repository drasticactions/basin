using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Basin.Render.Skia;
using SkiaSharp;

namespace Basin.Frames.Metacity;

public sealed class MetacityResources : IDisposable
{
    internal const int MiniIconSize = 16;
    internal const int IconSize = 48;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Dictionary<ImageKey, CachedImage> _images = [];
    private readonly Dictionary<MetacityDrawOp, (SKShader Shader, int Version)> _gradients = [];
    private readonly Dictionary<MetacityDrawOp, SKShader> _masks = [];
    private readonly Dictionary<FadeKey, SKShader> _fades = [];
    private readonly Dictionary<ArrowKey, SKVertices> _arrows = [];
    private readonly Dictionary<string, IconSource> _namedIcons = [];
    private readonly Dictionary<IBuffer, IconSource> _bufferIcons = [];
    private readonly Dictionary<(object Source, int Width, int Height), CachedImage> _scaledIcons = [];
    private readonly Dictionary<MetacityDrawOp, (SKPathEffect Effect, double Scale)> _dashes = [];
    private bool _disposed;

    public MetacityResources()
    {
        Fill = SkiaCensus.Track(new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill });
        Stroke = SkiaCensus.Track(new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 });
        BasinCounters.Track();
    }

    internal SKPaint Fill { get; }

    internal SKPaint Stroke { get; }

    private readonly SKPoint[] _arrowPoints = new SKPoint[3];
    private readonly SKColor[] _arrowColors = new SKColor[3];

    internal SKVertices Arrow(MetacityArrow arrow, int x, int y, int size, SKColor color)
    {
        _thread.Assert();
        var key = new ArrowKey(arrow, x, y, size, (uint)color);
        if (_arrows.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_arrows.Count >= 64)
        {
            foreach (var old in _arrows.Values)
            {
                SkiaCensus.Release(old);
            }

            _arrows.Clear();
        }

        var half = size / 2f;
        var points = _arrowPoints;
        switch (arrow)
        {
            case MetacityArrow.Up:
                points[0] = new SKPoint(x + half, y);
                points[1] = new SKPoint(x + size, y + size);
                points[2] = new SKPoint(x, y + size);
                break;
            case MetacityArrow.Down:
                points[0] = new SKPoint(x, y);
                points[1] = new SKPoint(x + size, y);
                points[2] = new SKPoint(x + half, y + size);
                break;
            case MetacityArrow.Left:
                points[0] = new SKPoint(x + size, y);
                points[1] = new SKPoint(x + size, y + size);
                points[2] = new SKPoint(x, y + half);
                break;
            default:
                points[0] = new SKPoint(x, y);
                points[1] = new SKPoint(x + size, y + half);
                points[2] = new SKPoint(x, y + size);
                break;
        }

        _arrowColors[0] = _arrowColors[1] = _arrowColors[2] = color;
        var vertices = SkiaCensus.Track(SKVertices.CreateCopy(SKVertexMode.Triangles, points, _arrowColors));
        _arrows[key] = vertices;
        return vertices;
    }

    public string? IconTheme { get; set; }

    private readonly record struct ImageKey(MetacityImageInfo Info, int Width, int Height, uint Colorize);

    private readonly record struct FadeKey(MetacityDrawOp Op, int X, int Y, int Space, int Height, uint Color);

    private readonly record struct ArrowKey(MetacityArrow Arrow, int X, int Y, int Size, uint Color);

    private sealed class CachedImage
    {
        public required SKImage Image { get; init; }

        public SKShader? Repeat { get; set; }
    }

    private sealed class IconSource
    {
        public string? SvgPath { get; init; }

        public SKImage? Bitmap { get; init; }

        public bool IsEmpty => SvgPath is null && Bitmap is null;
    }

    internal SKImage? Image(MetacityImageInfo info, int width, int height, uint colorize, out SKShader? repeat, bool repeating)
    {
        _thread.Assert();
        var key = new ImageKey(info, width, height, colorize);
        if (!_images.TryGetValue(key, out var cached))
        {
            var image = Decode(info, width, height, colorize);
            if (image is null)
            {
                repeat = null;
                return null;
            }

            cached = new CachedImage { Image = SkiaCensus.Track(image) };
            _images[key] = cached;
        }

        if (repeating && cached.Repeat is null)
        {
            cached.Repeat = SkiaCensus.Track(SKShader.CreateImage(cached.Image, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Nearest)));
        }

        repeat = repeating ? cached.Repeat : null;
        return cached.Image;
    }

    private static SKImage? Decode(MetacityImageInfo info, int width, int height, uint colorize)
    {
        if (info.IsSvg && colorize == 0)
        {
            using var rasterized = MetacityImageProbe.RasterizeSvg(info.Path, width, height);
            return rasterized is null ? null : SKImage.FromBitmap(rasterized);
        }

        using var source = info.IsSvg ? MetacityImageProbe.RasterizeSvg(info.Path) : SKBitmap.Decode(info.Path);
        if (source is null)
        {
            return null;
        }

        using var colored = colorize != 0 ? Colorize(source, colorize) : null;
        using var image = SKImage.FromBitmap(colored ?? source);
        return image is null ? null : Resample(image, width, height);
    }

    internal static SKImage? Resample(SKImage source, int width, int height)
    {
        if (source.Width == width && source.Height == height)
        {
            return SKImage.FromBitmap(SKBitmap.FromImage(source));
        }

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            return null;
        }

        var integerUp = width >= source.Width && height >= source.Height && width % source.Width == 0 && height % source.Height == 0;
        var down = width <= source.Width && height <= source.Height;
        var sampling = integerUp ? new SKSamplingOptions(SKFilterMode.Nearest)
            : down ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(source, new SKRect(0, 0, width, height), sampling);
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    private static unsafe SKBitmap Colorize(SKBitmap source, uint argb)
    {
        var red = ((argb >> 16) & 0xFF) / 255.0;
        var green = ((argb >> 8) & 0xFF) / 255.0;
        var blue = (argb & 0xFF) / 255.0;
        var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        using var unpremul = source.Copy(SKColorType.Bgra8888) ?? source;
        var src = (byte*)unpremul.GetPixels();
        var dst = (byte*)result.GetPixels();
        var premul = unpremul.AlphaType == SKAlphaType.Premul;
        for (var y = 0; y < source.Height; y++)
        {
            var s = src + y * unpremul.RowBytes;
            var d = dst + y * result.RowBytes;
            for (var x = 0; x < source.Width; x++, s += 4, d += 4)
            {
                double b = s[0], g = s[1], r = s[2];
                var a = s[3];
                if (premul && a > 0 && a < 255)
                {
                    b = b * 255 / a;
                    g = g * 255 / a;
                    r = r * 255 / a;
                }

                var intensity = (r * 0.30 + g * 0.59 + b * 0.11) / 255.0;
                double dr, dg, db;
                if (intensity <= 0.5)
                {
                    dr = red * intensity * 2.0;
                    dg = green * intensity * 2.0;
                    db = blue * intensity * 2.0;
                }
                else
                {
                    dr = red + (1.0 - red) * (intensity - 0.5) * 2.0;
                    dg = green + (1.0 - green) * (intensity - 0.5) * 2.0;
                    db = blue + (1.0 - blue) * (intensity - 0.5) * 2.0;
                }

                d[0] = (byte)Math.Clamp((int)(255 * db), 0, 255);
                d[1] = (byte)Math.Clamp((int)(255 * dg), 0, 255);
                d[2] = (byte)Math.Clamp((int)(255 * dr), 0, 255);
                d[3] = a;
            }
        }

        return result;
    }

    internal SKShader Gradient(MetacityDrawOp op, ReadOnlySpan<SKColor> colors, int version)
    {
        _thread.Assert();
        if (_gradients.TryGetValue(op, out var cached) && cached.Version == version)
        {
            return cached.Shader;
        }

        if (cached.Shader is not null)
        {
            SkiaCensus.Release(cached.Shader);
        }

        var gradient = op.Gradient!;
        var count = gradient.Colors.Count;
        var stops = new SKColor[count];
        var positions = new float[count];
        for (var i = 0; i < count; i++)
        {
            var color = colors[gradient.Colors[i].Index];
            if (op.Alpha is { } alpha)
            {
                var a = alpha.Alphas.Length == 1 ? alpha.Alphas[0] : alpha.Alphas[Math.Min(i, alpha.Alphas.Length - 1)];
                color = color.WithAlpha(a);
            }

            stops[i] = color;
            positions[i] = i / (float)(count - 1);
        }

        var end = gradient.Type switch
        {
            MetacityGradientType.Horizontal => new SKPoint(1, 0),
            MetacityGradientType.Vertical => new SKPoint(0, 1),
            _ => new SKPoint(1, 1),
        };
        var shader = SkiaCensus.Track(SKShader.CreateLinearGradient(new SKPoint(0, 0), end, stops, positions, SKShaderTileMode.Clamp));
        _gradients[op] = (shader, version);
        return shader;
    }

    internal SKShader TintGradient(MetacityDrawOp op, SKColor color, int version)
    {
        _thread.Assert();
        if (_gradients.TryGetValue(op, out var cached) && cached.Version == version)
        {
            return cached.Shader;
        }

        if (cached.Shader is not null)
        {
            SkiaCensus.Release(cached.Shader);
        }

        var alphas = op.Alpha!.Alphas;
        var stops = new SKColor[alphas.Length];
        var positions = new float[alphas.Length];
        for (var i = 0; i < alphas.Length; i++)
        {
            stops[i] = color.WithAlpha(alphas[i]);
            positions[i] = i / (float)(alphas.Length - 1);
        }

        var shader = SkiaCensus.Track(SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0), stops, positions, SKShaderTileMode.Clamp));
        _gradients[op] = (shader, version);
        return shader;
    }

    internal SKShader Mask(MetacityDrawOp op)
    {
        _thread.Assert();
        if (_masks.TryGetValue(op, out var cached))
        {
            return cached;
        }

        var alphas = op.Alpha!.Alphas;
        var stops = new SKColor[alphas.Length];
        var positions = new float[alphas.Length];
        for (var i = 0; i < alphas.Length; i++)
        {
            stops[i] = SKColors.Black.WithAlpha(alphas[i]);
            positions[i] = i / (float)(alphas.Length - 1);
        }

        var shader = SkiaCensus.Track(SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0), stops, positions, SKShaderTileMode.Clamp));
        _masks[op] = shader;
        return shader;
    }

    internal SKPathEffect Dash(MetacityDrawOp op, double scale)
    {
        _thread.Assert();
        if (_dashes.TryGetValue(op, out var cached) && cached.Scale == scale)
        {
            return cached.Effect;
        }

        if (cached.Effect is not null)
        {
            SkiaCensus.Release(cached.Effect);
        }

        var effect = SkiaCensus.Track(SKPathEffect.CreateDash([(float)(op.DashOn * scale), (float)(op.DashOff * scale)], 0));
        _dashes[op] = (effect, scale);
        return effect;
    }

    internal SKShader Fade(MetacityDrawOp op, int x, int y, int space, int height, SKColor color)
    {
        _thread.Assert();
        var key = new FadeKey(op, x, y, space, height, (uint)color);
        if (_fades.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_fades.Count >= 64)
        {
            foreach (var fade in _fades.Values)
            {
                SkiaCensus.Release(fade);
            }

            _fades.Clear();
        }

        var startAlpha = (float)(1.0 - 30.0 / space);
        var shader = SkiaCensus.Track(SKShader.CreateLinearGradient(
            new SKPoint(x, y),
            new SKPoint(space, height),
            [color, color, color.WithAlpha(0)],
            [0f, startAlpha, 1f],
            SKShaderTileMode.Clamp));
        _fades[key] = shader;
        return shader;
    }

    internal bool HasIcon(in FrameState state) => Source(state) is { IsEmpty: false };

    internal SKImage? Icon(in FrameState state, int width, int height, out SKShader? repeat, bool repeating)
    {
        _thread.Assert();
        repeat = null;
        if (Source(state) is not { IsEmpty: false } source)
        {
            return null;
        }

        var key = ((object)source, width, height);
        if (!_scaledIcons.TryGetValue(key, out var cached))
        {
            SKImage? image;
            if (source.SvgPath is { } svg)
            {
                using var rasterized = MetacityImageProbe.RasterizeSvg(svg, width, height);
                image = rasterized is null ? null : SKImage.FromBitmap(rasterized);
            }
            else
            {
                image = Resample(source.Bitmap!, width, height);
            }

            if (image is null)
            {
                return null;
            }

            cached = new CachedImage { Image = SkiaCensus.Track(image) };
            _scaledIcons[key] = cached;
        }

        if (repeating && cached.Repeat is null)
        {
            cached.Repeat = SkiaCensus.Track(SKShader.CreateImage(cached.Image, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Nearest)));
        }

        repeat = repeating ? cached.Repeat : null;
        return cached.Image;
    }

    private IconSource? Source(in FrameState state)
    {
        if (state.Icon.Pixels is { } pixels)
        {
            return BufferSource(pixels);
        }

        var name = state.Icon.Name is { Length: > 0 } named ? named : state.AppId;
        if (name is not { Length: > 0 })
        {
            return null;
        }

        if (!_namedIcons.TryGetValue(name, out var source))
        {
            source = FromName(name);
            _namedIcons[name] = source;
        }

        return source;
    }

    private IconSource BufferSource(IBuffer pixels)
    {
        if (_bufferIcons.TryGetValue(pixels, out var source))
        {
            return source;
        }

        source = FromBuffer(pixels);
        _bufferIcons[pixels] = source;
        pixels.Destroyed += () =>
        {
            if (_bufferIcons.Remove(pixels, out var dead))
            {
                ForgetScaled(dead);
                SkiaCensus.Release(dead.Bitmap);
            }
        };
        return source;
    }

    private void ForgetScaled(IconSource source)
    {
        List<(object Source, int Width, int Height)>? dead = null;
        foreach (var entry in _scaledIcons)
        {
            if (ReferenceEquals(entry.Key.Source, source))
            {
                (dead ??= []).Add(entry.Key);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var key in dead)
        {
            if (_scaledIcons.Remove(key, out var cached))
            {
                SkiaCensus.Release(cached.Repeat);
                SkiaCensus.Release(cached.Image);
            }
        }
    }

    private static IconSource FromBuffer(IBuffer buffer)
    {
        if (!SkiaRenderer.TryImageInfo(buffer.Width, buffer.Height, DrmFormat.Argb8888, out var info) ||
            !buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return new IconSource();
        }

        var image = SKImage.FromPixelCopy(info, view.Data, view.Stride);
        buffer.EndDataAccess();
        return new IconSource { Bitmap = image is null ? null : SkiaCensus.Track(image) };
    }

    private IconSource FromName(string name)
    {
        _search ??= new IconSearch { Sizes = [48, 32, 16], Theme = IconTheme };
        if (_search.Find(name) is not { } path)
        {
            return new IconSource();
        }

        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return new IconSource { SvgPath = path };
        }

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null)
        {
            return new IconSource();
        }

        var image = SKImage.FromBitmap(bitmap);
        return new IconSource { Bitmap = image is null ? null : SkiaCensus.Track(image) };
    }

    private IconSearch? _search;

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var image in _images.Values)
        {
            SkiaCensus.Release(image.Repeat);
            SkiaCensus.Release(image.Image);
        }

        _images.Clear();
        foreach (var gradient in _gradients.Values)
        {
            SkiaCensus.Release(gradient.Shader);
        }

        _gradients.Clear();
        foreach (var mask in _masks.Values)
        {
            SkiaCensus.Release(mask);
        }

        _masks.Clear();
        foreach (var dash in _dashes.Values)
        {
            SkiaCensus.Release(dash.Effect);
        }

        _dashes.Clear();
        foreach (var fade in _fades.Values)
        {
            SkiaCensus.Release(fade);
        }

        _fades.Clear();
        foreach (var arrow in _arrows.Values)
        {
            SkiaCensus.Release(arrow);
        }

        _arrows.Clear();
        foreach (var scaled in _scaledIcons.Values)
        {
            SkiaCensus.Release(scaled.Repeat);
            SkiaCensus.Release(scaled.Image);
        }

        _scaledIcons.Clear();
        foreach (var source in _namedIcons.Values)
        {
            SkiaCensus.Release(source.Bitmap);
        }

        _namedIcons.Clear();
        foreach (var source in _bufferIcons.Values)
        {
            SkiaCensus.Release(source.Bitmap);
        }

        _bufferIcons.Clear();
        SkiaCensus.Release(Stroke);
        SkiaCensus.Release(Fill);
        BasinCounters.Untrack();
    }
}
