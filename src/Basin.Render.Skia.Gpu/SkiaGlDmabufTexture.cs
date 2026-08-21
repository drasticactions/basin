using Basin.Render.Gl;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaGlDmabufTexture : ISkiaTexture
{
    private readonly GlDmabufTexture _native;
    private readonly GRBackendTexture _backend;
    private readonly SKImage _image;

    private SkiaGlDmabufTexture(GlDmabufTexture native, GRBackendTexture backend, SKImage image)
    {
        _native = native;
        _backend = backend;
        _image = image;
        Width = native.Width;
        Height = native.Height;
    }

    public int Width { get; }

    public int Height { get; }

    internal static SkiaGlDmabufTexture? TryImport(SkiaGlRenderer renderer, in DmabufAttributes attributes)
    {
        var native = GlDmabufTexture.TryImport(renderer.Device, attributes);
        if (native is null)
        {
            return null;
        }

        var backend = SkiaCensus.Track(new GRBackendTexture(
            native.Width, native.Height, mipmapped: false,
            new GRGlTextureInfo(SkiaGlRenderer.GlTexture2D, native.TextureId, SkiaGlRenderer.GlBgra8)));
        var image = SKImage.FromTexture(
            renderer.Context, backend, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888,
            native.HasAlpha ? SKAlphaType.Premul : SKAlphaType.Opaque);
        if (image is null)
        {
            SkiaCensus.Release(backend);
            native.Dispose();
            return null;
        }

        return new SkiaGlDmabufTexture(native, backend, SkiaCensus.Track(image));
    }

    internal GlDmabufTexture Native => _native;

    internal bool SampledThisPass { get; set; }

    public bool Acquire(out SKImage image)
    {
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
        _native.Dispose();
    }
}
