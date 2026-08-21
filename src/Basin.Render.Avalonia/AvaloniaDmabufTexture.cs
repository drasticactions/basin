using Basin.Render.Skia;
using SkiaSharp;

namespace Basin.Render.Avalonia;

internal sealed class AvaloniaDmabufTexture : ISkiaTexture
{
    private const uint GlTexture2D = 0x0DE1;
    private const uint GlBgra8 = 0x93A1;
    private const uint GlRgba8 = 0x8058;

    private static uint _glFormat = GlBgra8;

    private readonly AvaloniaRenderer _renderer;
    private readonly uint _texture;
    private readonly nint _image;
    private readonly GRBackendTexture _backend;
    private readonly SKImage _skImage;
    private readonly int _generation;

    private AvaloniaDmabufTexture(
        AvaloniaRenderer renderer, uint texture, nint image, GRBackendTexture backend, SKImage skImage,
        int width, int height, int generation)
    {
        _renderer = renderer;
        _texture = texture;
        _image = image;
        _backend = backend;
        _skImage = skImage;
        Width = width;
        Height = height;
        _generation = generation;
    }

    public int Width { get; }

    public int Height { get; }

    internal static AvaloniaDmabufTexture? TryImport(
        AvaloniaRenderer renderer, AvaloniaEglImport egl, GRContext context, in DmabufAttributes attributes)
    {
        if (egl.Import(attributes) is not { } native)
        {
            return null;
        }

        var alphaType = attributes.Format.HasAlpha() ? SKAlphaType.Premul : SKAlphaType.Opaque;
        var backend = SkiaCensus.Track(new GRBackendTexture(
            attributes.Width, attributes.Height, mipmapped: false,
            new GRGlTextureInfo(GlTexture2D, native.Texture, _glFormat)));
        var image = SKImage.FromTexture(context, backend, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888, alphaType);
        if (image is null)
        {
            var other = _glFormat == GlBgra8 ? GlRgba8 : GlBgra8;
            SkiaCensus.Release(backend);
            backend = SkiaCensus.Track(new GRBackendTexture(
                attributes.Width, attributes.Height, mipmapped: false,
                new GRGlTextureInfo(GlTexture2D, native.Texture, other)));
            image = SKImage.FromTexture(context, backend, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888, alphaType);
            if (image is not null)
            {
                _glFormat = other;
            }
        }

        if (image is null)
        {
            Basin.Diagnostics.BasinLog.Debug(
                $"dmabuf texture rejected by skia: {attributes.Width}x{attributes.Height} fmt={attributes.Format}");
            SkiaCensus.Release(backend);
            egl.Destroy(native.Texture, native.Image);
            return null;
        }

        return new AvaloniaDmabufTexture(
            renderer, native.Texture, native.Image, backend, SkiaCensus.Track(image),
            attributes.Width, attributes.Height, renderer.ContextGeneration);
    }

    public bool Acquire(out SKImage image)
    {
        if (_generation != _renderer.ContextGeneration)
        {
            image = null!;
            return false;
        }

        image = _skImage;
        return true;
    }

    public void Release()
    {
    }

    public void Dispose()
    {
        SkiaCensus.Release(_skImage);
        SkiaCensus.Release(_backend);
        _renderer.ScheduleEglRelease(_texture, _image, _generation);
    }
}
