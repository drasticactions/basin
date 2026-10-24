using Basin.Render.Skia;
using Basin.Themes.MsStyles;
using SkiaSharp;

namespace Basin.Frames.MsStyles;

/// <summary>
/// The Skia objects that <see cref="MsStylesSkiaPainter"/> draws with: images made from the image cache, frame subsets, gradient shaders, icons and pooled paints. Many windows can share one instance.
/// </summary>
public sealed class MsStylesResources : IDisposable
{
    private readonly List<SKImage?> _images = [];
    private readonly Dictionary<MsStylesGradient, SKShader> _gradients = [];
    private readonly Dictionary<(int Handle, System.Drawing.Rectangle Frame), SKImage?> _frames = [];
    private readonly Dictionary<Basin.IBuffer, SKImage?> _pixelIcons = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesResources"/> class.
    /// </summary>
    /// <param name="images">The image cache that the draw lists refer to. This instance does not own it.</param>
    public MsStylesResources(MsStylesImageCache images)
    {
        ArgumentNullException.ThrowIfNull(images);
        Images = images;
        ImagePaint = SkiaCensus.Track(new SKPaint());
        FillPaint = SkiaCensus.Track(new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill });
        StrokePaint = SkiaCensus.Track(new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke });
    }

    /// <summary>
    /// Gets the image cache that the draw lists refer to.
    /// </summary>
    public MsStylesImageCache Images { get; }

    internal SKPaint ImagePaint { get; }

    internal SKPaint FillPaint { get; }

    internal SKPaint StrokePaint { get; }

    /// <summary>
    /// Gets or sets the text engine for text and glow operations. Without one, the painter draws no text. <see cref="Dispose"/> disposes it.
    /// </summary>
    public MsStylesSkiaText? Text { get; set; }

    /// <summary>
    /// Gets or sets the lookup for named icons. It takes an icon name and a scale in multiples of 16 pixels, and returns
    /// <see langword="null"/> when no icon matches. The caller keeps ownership of each returned image.
    /// </summary>
    public Func<string, int, SKImage?>? Icons { get; set; }

    /// <summary>
    /// Gets the image for a window icon. Client pixels come first, then the icon name, then the application ID through <see cref="Icons"/>.
    /// </summary>
    /// <param name="icon">The window icon.</param>
    /// <param name="appId">The application ID to look up when the icon has no name.</param>
    /// <param name="pixels">The icon size in device pixels.</param>
    /// <returns>The image, or <see langword="null"/> when nothing matches.</returns>
    public SKImage? IconFor(in Basin.Capabilities.FrameIcon icon, string? appId, int pixels)
    {
        if (icon.Pixels is { } buffer && ImageFor(buffer) is { } client)
        {
            return client;
        }

        var name = icon.Name is { Length: > 0 } named ? named : appId;
        return name is { Length: > 0 } ? Icons?.Invoke(name, Math.Max(1, (pixels + 15) / 16)) : null;
    }

    /// <summary>
    /// Copies an icon buffer into a Skia image once. The copy stays cached until the buffer is destroyed.
    /// </summary>
    /// <param name="buffer">An ARGB8888 buffer that allows read access.</param>
    /// <returns>The image, owned by this instance, or <see langword="null"/> when the buffer cannot be read.</returns>
    public SKImage? ImageFor(Basin.IBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (_pixelIcons.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        SKImage? image = null;
        if (SkiaRenderer.TryImageInfo(buffer.Width, buffer.Height, Basin.DrmFormat.Argb8888, out var info) &&
            buffer.BeginDataAccess(Basin.BufferDataAccess.Read, out var view))
        {
            var copied = SKImage.FromPixelCopy(info, view.Data, view.Stride);
            buffer.EndDataAccess();
            if (copied is not null)
            {
                image = SkiaCensus.Track(copied);
            }
        }

        _pixelIcons[buffer] = image;
        buffer.Destroyed += () => ForgetIcon(buffer);
        return image;
    }

    private void ForgetIcon(Basin.IBuffer buffer)
    {
        if (_pixelIcons.Remove(buffer, out var dead) && dead is not null)
        {
            SkiaCensus.Release(dead);
        }
    }

    /// <summary>
    /// Gets the Skia image for a bitmap in the image cache, and makes it on the first request.
    /// </summary>
    /// <param name="handle">The <see cref="MsStylesBitmap.Handle"/>.</param>
    /// <returns>The image, owned by this instance, or <see langword="null"/> when Skia cannot make it.</returns>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public SKImage? Image(int handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (_images.Count <= handle)
        {
            _images.Add(null);
        }

        if (_images[handle] is { } cached)
        {
            return cached;
        }

        var bitmap = Images[handle];
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        SKImage? image;
        unsafe
        {
            fixed (byte* pixels = bitmap.Pixels)
            {
                using var pixmap = new SKPixmap(info, (nint)pixels, info.RowBytes);
                image = SKImage.FromPixelCopy(pixmap);
            }
        }

        if (image is null)
        {
            return null;
        }

        _images[handle] = SkiaCensus.Track(image);
        return image;
    }

    /// <summary>
    /// Gets a cached subset image for one cell of a bitmap, so linear sampling at a cell edge cannot read the next cell.
    /// </summary>
    /// <param name="handle">The <see cref="MsStylesBitmap.Handle"/>.</param>
    /// <param name="frame">The cell in texels. A cell that covers the whole bitmap gives the whole image.</param>
    /// <returns>The image, owned by this instance, or <see langword="null"/> when Skia cannot make it.</returns>
    public SKImage? Frame(int handle, System.Drawing.Rectangle frame)
    {
        if (Image(handle) is not { } image)
        {
            return null;
        }

        if (frame.X == 0 && frame.Y == 0 && frame.Width == image.Width && frame.Height == image.Height)
        {
            return image;
        }

        if (_frames.TryGetValue((handle, frame), out var cached))
        {
            return cached;
        }

        var subset = image.Subset(new SKRectI(frame.Left, frame.Top, frame.Right, frame.Bottom));
        _frames[(handle, frame)] = subset is null ? null : SkiaCensus.Track(subset);
        return subset;
    }

    internal SKShader Gradient(in MsStylesGradient gradient)
    {
        if (_gradients.TryGetValue(gradient, out var shader))
        {
            return shader;
        }

        var count = Math.Max(gradient.Count, 1);
        var colors = new SKColor[count == 1 ? 2 : count];
        var positions = new float[colors.Length];
        var ratios = true;
        for (var i = 0; i < count; i++)
        {
            ratios &= gradient.RatioAt(i) >= 0;
        }

        for (var i = 0; i < colors.Length; i++)
        {
            var index = Math.Min(i, count - 1);
            colors[i] = new SKColor(gradient.ColorAt(index));
            positions[i] = count == 1 ? i : ratios ? gradient.RatioAt(index) / 255f : i / (float)(colors.Length - 1);
        }

        shader = gradient.Kind switch
        {
            FillType.HorizontalGradient => SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0), colors, positions, SKShaderTileMode.Clamp),
            FillType.RadialGradient => SKShader.CreateRadialGradient(new SKPoint(0.5f, 0.5f), 0.5f, colors, positions, SKShaderTileMode.Clamp),
            _ => SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 1), colors, positions, SKShaderTileMode.Clamp),
        };
        _gradients[gradient] = SkiaCensus.Track(shader);
        return shader;
    }

    /// <summary>
    /// Releases every Skia object that this instance made, and disposes <see cref="Text"/>. It does not release the images that <see cref="Icons"/> returned.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var image in _images)
        {
            SkiaCensus.Release(image);
        }

        _images.Clear();
        foreach (var frame in _frames.Values)
        {
            SkiaCensus.Release(frame);
        }

        _frames.Clear();
        foreach (var shader in _gradients.Values)
        {
            SkiaCensus.Release(shader);
        }

        _gradients.Clear();
        foreach (var icon in _pixelIcons.Values)
        {
            SkiaCensus.Release(icon);
        }

        _pixelIcons.Clear();
        Text?.Dispose();
        SkiaCensus.Release(ImagePaint);
        SkiaCensus.Release(FillPaint);
        SkiaCensus.Release(StrokePaint);
    }
}
