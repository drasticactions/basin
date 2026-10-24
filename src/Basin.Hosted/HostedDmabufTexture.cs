using Basin.Diagnostics;
using Basin.Render.Skia;
using SkiaSharp;
using static Basin.Hosted.HostedLog;

namespace Basin.Hosted;

internal sealed class HostedDmabufTexture : ISkiaTexture
{
    private readonly SkiaImageShader _meshShader = new();

    private const uint GlTexture2D = 0x0DE1;
    private const uint GlBgra8 = 0x93A1;
    private const uint GlRgba8 = 0x8058;

    private static uint _glFormat = GlBgra8;

    private readonly HostedRenderer _renderer;
    private readonly IBuffer _buffer;
    private HostedEglImport _import;
    private uint _texture;
    private nint _image;
    private GRBackendTexture? _backend;
    private SKImage? _skImage;
    private int _generation;
    private int _refusedGeneration = -1;

    private HostedDmabufTexture(HostedRenderer renderer, IBuffer buffer, HostedEglImport import, int width, int height)
    {
        _renderer = renderer;
        _buffer = buffer;
        _import = import;
        _generation = -1;
        Width = width;
        Height = height;
    }

    private HostedDmabufTexture(HostedRenderer renderer, IBuffer buffer, HostedEglImport import, in Imported imported, int width, int height)
    {
        _renderer = renderer;
        _buffer = buffer;
        _import = import;
        _texture = imported.Texture;
        _image = imported.Image;
        _backend = imported.Backend;
        _skImage = imported.SkImage;
        _generation = renderer.ContextGeneration;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    internal static HostedDmabufTexture? TryImport(
        HostedRenderer renderer, IBuffer buffer, HostedEglImport egl, GRContext context, in DmabufAttributes attributes) =>
        Import(egl, context, attributes) is { } imported
            ? new HostedDmabufTexture(renderer, buffer, egl, imported, attributes.Width, attributes.Height)
            : null;

    internal static HostedDmabufTexture Deferred(
        HostedRenderer renderer, IBuffer buffer, HostedEglImport egl, in DmabufAttributes attributes) =>
        new(renderer, buffer, egl, attributes.Width, attributes.Height);

    public bool Acquire(out SKImage image)
    {
        if ((_generation != _renderer.ContextGeneration || _skImage is null) && !Reimport())
        {
            image = null!;
            return false;
        }

        image = _skImage!;
        return true;
    }

    public void Release()
    {
    }

    public SKShader? MeshShader(SKImage image) => _meshShader.For(image);

    public void Dispose()
    {
        _meshShader.Drop();
        DropImported();
    }

    private void DropImported()
    {
        if (_skImage is null)
        {
            return;
        }

        SkiaCensus.Release(_skImage);
        SkiaCensus.Release(_backend);
        _renderer.ScheduleEglRelease(_texture, _image, _generation, _import);
        _skImage = null;
        _backend = null;
    }

    private bool Reimport()
    {
        var generation = _renderer.ContextGeneration;
        if (_refusedGeneration == generation ||
            _renderer.EglImport is not { } egl ||
            _renderer.DrawingGpuContext is not { } context ||
            _buffer.IsDestroyed ||
            !_buffer.TryGetDmabuf(out var attributes) ||
            !egl.Formats.Contains(attributes.Format))
        {
            return false;
        }

        AllocationScope.Pause();
        try
        {
            if (Import(egl, context, attributes) is not { } imported)
            {
                _refusedGeneration = generation;
                return false;
            }

            _meshShader.Drop();
            DropImported();
            _import = egl;
            _texture = imported.Texture;
            _image = imported.Image;
            _backend = imported.Backend;
            _skImage = imported.SkImage;
            _generation = generation;
            return true;
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private static Imported? Import(HostedEglImport egl, GRContext context, in DmabufAttributes attributes)
    {
        var imported = egl.Import(attributes);
        context.ResetContext(GRGlBackendState.TextureBinding);
        if (imported is not { } native)
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
            Log.Debug(
                $"dmabuf: skia rejected a {attributes.Width}x{attributes.Height} texture, format {attributes.Format}");
            SkiaCensus.Release(backend);
            egl.Destroy(native.Texture, native.Image);
            return null;
        }

        return new Imported(native.Texture, native.Image, backend, SkiaCensus.Track(image));
    }

    private readonly record struct Imported(uint Texture, nint Image, GRBackendTexture Backend, SKImage SkImage);
}
