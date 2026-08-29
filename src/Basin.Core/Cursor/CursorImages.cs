using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Diagnostics.CoreLog;

namespace Basin;

public sealed class CursorImages : IDisposable
{
    private readonly IAllocator _allocator;
    private readonly int _bufferWidth;
    private readonly int _bufferHeight;
    private readonly int _logicalSize;
    private readonly List<Variant> _variants = [];
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private Variant _current;
    private IBuffer? _clientImage;

    public CursorImages(
        IAllocator allocator,
        int bufferWidth,
        int bufferHeight,
        string? themeName = null,
        int logicalSize = 24,
        double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        _allocator = allocator;
        _bufferWidth = bufferWidth;
        _bufferHeight = bufferHeight;
        _logicalSize = logicalSize;
        ThemeName = themeName;
        _current = Entry(new CursorKey(scale, null));
    }

    private sealed class Variant
    {
        public required CursorKey Key;

        public required int Size;

        public required XcursorTheme? Theme;

        public required ColorLut3D? Lut;

        public Dictionary<string, CursorImage?> Cache { get; } = [];
    }

    public IColorProfileService? ColorProfiles { get; set; }

    public string? ThemeName { get; }

    public int Size => _current.Size;

    public bool HasTheme => _current.Theme is not null;

    public int VariantCount => _variants.Count;

    public int SizeForScale(double scale) => Math.Max(1, (int)(_logicalSize * OutputScaling.Snap(scale)));

    public bool Use(in CursorKey key, Action reacquire)
    {
        ArgumentNullException.ThrowIfNull(reacquire);
        _thread.Assert();
        var entry = Entry(key);
        if (ReferenceEquals(entry, _current))
        {
            return false;
        }

        _current = entry;
        reacquire();
        return true;
    }

    public bool ReloadForScale(double scale, Action reacquire) =>
        Use(_current.Key with { Scale = scale }, reacquire);

    public CursorImage? Named(string name) => Named(name, _current.Key);

    public CursorImage? Named(string name, in CursorKey key)
    {
        _thread.Assert();
        var entry = Entry(key);
        _current = entry;
        if (entry.Cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        CursorImage? image = null;
        if (entry.Theme is { } theme)
        {
            var cursor = theme.Get(name);
            foreach (var alias in CursorAliases.Of(name))
            {
                cursor ??= theme.Get(alias);
            }

            if (cursor is not null)
            {
                var frame = cursor.Frame(0);
                var oversized = frame.Width > _bufferWidth || frame.Height > _bufferHeight;
                if (oversized && Allocate() is { } scaled)
                {
                    var factor = Math.Min(
                        _bufferWidth / (double)frame.Width, _bufferHeight / (double)frame.Height);
                    var width = Math.Max(1, (int)Math.Round(frame.Width * factor));
                    var height = Math.Max(1, (int)Math.Round(frame.Height * factor));
                    if (UploadScaled(scaled, frame.Pixels, frame.Width, frame.Height, width, height, entry.Lut))
                    {
                        Log.Debug(
                            $"cursor {name} is {frame.Width}x{frame.Height} at scale {entry.Key.Scale} " +
                            $"and the buffer is {scaled.Width}x{scaled.Height}, so it is scaled to fit the cursor plane");

                        image = new CursorImage(
                            scaled,
                            width,
                            height,
                            (int)Math.Round(frame.HotspotX * factor),
                            (int)Math.Round(frame.HotspotY * factor));
                    }
                    else
                    {
                        (scaled as BufferBase)?.Destroy();
                    }
                }

                if (image is null && AllocateFor(frame.Width, frame.Height) is { } buffer)
                {
                    if (Upload(buffer, frame.Pixels, frame.Width, frame.Height, entry.Lut))
                    {
                        if (oversized)
                        {
                            Log.Debug(
                                $"cursor {name} is {frame.Width}x{frame.Height} at scale {entry.Key.Scale} " +
                                $"and the buffer is {buffer.Width}x{buffer.Height}, so it cannot go on a cursor plane");
                        }

                        image = new CursorImage(
                            buffer,
                            Math.Min(frame.Width, buffer.Width),
                            Math.Min(frame.Height, buffer.Height),
                            frame.HotspotX,
                            frame.HotspotY,
                            oversized);
                    }
                    else
                    {
                        (buffer as BufferBase)?.Destroy();
                    }
                }
            }
        }

        entry.Cache[name] = image;
        return image;
    }

    private Variant Entry(in CursorKey key)
    {
        var scale = OutputScaling.Snap(key.Scale);
        foreach (var variant in _variants)
        {
            if (variant.Key.Scale == scale &&
                ImageDescription.ContentComparer.Equals(variant.Key.Output, key.Output))
            {
                return variant;
            }
        }

        var size = SizeForScale(scale);
        var created = new Variant
        {
            Key = new CursorKey(scale, key.Output),
            Size = size,
            Theme = XcursorTheme.Load(ThemeName, size) ?? _current?.Theme,
            Lut = key.Output is { } output && ColorProfiles is { } profiles
                ? profiles.BuildLut(ImageDescription.Srgb, output)
                : null,
        };

        _variants.Add(created);
        return created;
    }

    public CursorImage? FromSurface(IBuffer source, int hotspotX, int hotspotY, double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(source);
        _thread.Assert();
        if (!source.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return null;
        }

        try
        {
            _clientImage ??= Allocate();
            if (_clientImage is null || view.Stride != source.Width * 4)
            {
                return null;
            }

            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            unsafe
            {
                var pixels = new ReadOnlySpan<byte>((void*)view.Data, view.Stride * source.Height);
                var uploaded = width == source.Width && height == source.Height
                    ? Upload(_clientImage, pixels, source.Width, source.Height, _current.Lut)
                    : UploadScaled(_clientImage, pixels, source.Width, source.Height, width, height, _current.Lut);
                if (!uploaded)
                {
                    return null;
                }
            }

            return new CursorImage(
                _clientImage,
                Math.Min(width, _clientImage.Width),
                Math.Min(height, _clientImage.Height),
                (int)Math.Round(hotspotX * scale),
                (int)Math.Round(hotspotY * scale));
        }
        finally
        {
            source.EndDataAccess();
        }
    }

    public void Dispose()
    {
        _thread.Assert();
        foreach (var variant in _variants)
        {
            foreach (var image in variant.Cache.Values)
            {
                (image?.Buffer as BufferBase)?.Destroy();
            }

            variant.Cache.Clear();
        }

        (_clientImage as BufferBase)?.Destroy();
        _clientImage = null;
    }

    private IBuffer? Allocate() =>
        _allocator.Allocate(_bufferWidth, _bufferHeight, DrmFormat.Argb8888, [DrmFormatSet.ModifierLinear], BufferUse.Cursor);

    private IBuffer? AllocateFor(int width, int height)
    {
        if (width <= _bufferWidth && height <= _bufferHeight)
        {
            return Allocate();
        }

        return _allocator.Allocate(
                width, height, DrmFormat.Argb8888, [DrmFormatSet.ModifierLinear], BufferUse.Cursor)
            ?? Allocate();
    }

    private static unsafe bool UploadScaled(
        IBuffer buffer, ReadOnlySpan<byte> pixels, int sourceWidth, int sourceHeight, int width, int height,
        ColorLut3D? lut = null)
    {
        if (!buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return false;
        }

        new Span<byte>((void*)view.Data, view.Stride * buffer.Height).Clear();
        var stepX = (double)sourceWidth / width;
        var stepY = (double)sourceHeight / height;
        for (var y = 0; y < Math.Min(height, buffer.Height); y++)
        {
            var sourceY = ((y + 0.5) * stepY) - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(sourceY), 0, sourceHeight - 1);
            var y1 = Math.Clamp(y0 + 1, 0, sourceHeight - 1);
            var fy = Math.Clamp(sourceY - y0, 0, 1);
            var row = (byte*)(view.Data + (y * view.Stride));
            for (var x = 0; x < Math.Min(width, buffer.Width); x++)
            {
                var sourceX = ((x + 0.5) * stepX) - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(sourceX), 0, sourceWidth - 1);
                var x1 = Math.Clamp(x0 + 1, 0, sourceWidth - 1);
                var fx = Math.Clamp(sourceX - x0, 0, 1);
                for (var channel = 0; channel < 4; channel++)
                {
                    var topLeft = pixels[(((y0 * sourceWidth) + x0) * 4) + channel];
                    var topRight = pixels[(((y0 * sourceWidth) + x1) * 4) + channel];
                    var bottomLeft = pixels[(((y1 * sourceWidth) + x0) * 4) + channel];
                    var bottomRight = pixels[(((y1 * sourceWidth) + x1) * 4) + channel];
                    var top = topLeft + ((topRight - topLeft) * fx);
                    var bottom = bottomLeft + ((bottomRight - bottomLeft) * fx);
                    row[(x * 4) + channel] = (byte)Math.Clamp(Math.Round(top + ((bottom - top) * fy)), 0, 255);
                }
            }
        }

        Convert(view.Data, view.Stride, Math.Min(width, buffer.Width), Math.Min(height, buffer.Height), lut);
        buffer.EndDataAccess();
        return true;
    }

    private static unsafe bool Upload(
        IBuffer buffer, ReadOnlySpan<byte> pixels, int width, int height, ColorLut3D? lut = null)
    {
        if (!buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return false;
        }

        new Span<byte>((void*)view.Data, view.Stride * buffer.Height).Clear();
        var copyWidth = Math.Min(width, buffer.Width);
        var copyHeight = Math.Min(height, buffer.Height);
        for (var y = 0; y < copyHeight; y++)
        {
            pixels.Slice(y * width * 4, copyWidth * 4)
                .CopyTo(new Span<byte>((void*)(view.Data + (y * view.Stride)), copyWidth * 4));
        }

        Convert(view.Data, view.Stride, copyWidth, copyHeight, lut);
        buffer.EndDataAccess();
        return true;
    }

    private static unsafe void Convert(nint data, int stride, int width, int height, ColorLut3D? lut)
    {
        if (lut is null)
        {
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var row = (byte*)(data + (y * stride));
            for (var x = 0; x < width; x++)
            {
                var pixel = row + (x * 4);
                var alpha = pixel[3];
                if (alpha == 0)
                {
                    continue;
                }

                var blue = Math.Clamp(pixel[0] / (float)alpha, 0f, 1f);
                var green = Math.Clamp(pixel[1] / (float)alpha, 0f, 1f);
                var red = Math.Clamp(pixel[2] / (float)alpha, 0f, 1f);
                var (r, g, b) = lut.Sample(red, green, blue);
                pixel[0] = (byte)Math.Clamp(MathF.Round(b * alpha), 0f, 255f);
                pixel[1] = (byte)Math.Clamp(MathF.Round(g * alpha), 0f, 255f);
                pixel[2] = (byte)Math.Clamp(MathF.Round(r * alpha), 0f, 255f);
            }
        }
    }
}
