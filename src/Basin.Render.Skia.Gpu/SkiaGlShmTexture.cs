using Basin.Render.Gl;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaGlShmTexture : ISkiaTexture, IRefreshableTexture
{
    private readonly SkiaGlRenderer _renderer;
    private readonly GlShmTexture _native;
    private GRBackendTexture? _backend;
    private SKImage? _image;
    private bool _clean;

    internal SkiaGlShmTexture(SkiaGlRenderer renderer, IBuffer buffer)
    {
        _renderer = renderer;
        _native = new GlShmTexture(renderer.Device, buffer);
        Width = buffer.Width;
        Height = buffer.Height;
    }

    public int Width { get; }

    public int Height { get; }

    public void MarkDirty()
    {
        _native.MarkDirty();
        _clean = false;
    }

    public bool Acquire(out SKImage image)
    {
        if (_clean && _image is not null)
        {
            image = _image;
            return true;
        }

        if (!_native.Acquire(out var textureId))
        {
            image = null!;
            return false;
        }

        _renderer.NotifyGlStateTouched();

        if (_image is null)
        {
            _backend = SkiaCensus.Track(new GRBackendTexture(
                Width, Height, mipmapped: false,
                new GRGlTextureInfo(SkiaGlRenderer.GlTexture2D, textureId, SkiaGlRenderer.GlBgra8)));
            var wrapped = SKImage.FromTexture(
                _renderer.Context, _backend, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888,
                _native.HasAlpha ? SKAlphaType.Premul : SKAlphaType.Opaque);
            if (wrapped is null)
            {
                SkiaCensus.Release(_backend);
                _backend = null;
                image = null!;
                return false;
            }

            _image = SkiaCensus.Track(wrapped);
        }

        _clean = true;
        image = _image;
        return true;
    }

    public void Release()
    {
    }

    public void Dispose()
    {
        SkiaCensus.Release(_image);
        SkiaCensus.Release(_backend);
        _image = null;
        _backend = null;
        _native.Dispose();
    }
}
