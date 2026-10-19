using System.Runtime.InteropServices;
using Basin.Render.Skia;
using SDL3;
using SkiaSharp;

namespace Basin.Sdl;

public sealed unsafe class SdlRasterLease : ISdlRenderLease
{
    private readonly SdlWindow _window;
    private nint _renderer;
    private nint _texture;
    private void* _pixels;
    private SKSurface? _surface;
    private SKCanvas? _canvas;
    private int _width;
    private int _height;
    private int _stride;
    private bool _drawn;
    private bool _disposed;

    public SdlRasterLease(SdlWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _renderer = SDL.CreateRenderer(window.Handle, null);
        if (_renderer == 0)
        {
            throw new InvalidOperationException($"SDL_CreateRenderer failed: {SDL.GetError()}");
        }

        _ = SDL.SetRenderVSync(_renderer, 1);
        RendererName = SDL.GetRendererName(_renderer) ?? string.Empty;
        var (width, height) = window.PixelSize;
        Resize(width, height);
    }

    public string Name => SdlRenderDevices.Raster;

    public string RendererName { get; }

    public SdlHdrState Hdr => SdlHdrState.Sdr;

    public void Resize(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        if (pixelWidth == _width && pixelHeight == _height && _surface is not null)
        {
            return;
        }

        ReleaseTarget();
        _width = pixelWidth;
        _height = pixelHeight;
        _stride = pixelWidth * 4;
        _pixels = NativeMemory.AllocZeroed((nuint)(_stride * pixelHeight));
        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        _surface = SkiaCensus.Track(SKSurface.Create(info, (nint)_pixels, _stride)
            ?? throw new InvalidOperationException($"no raster surface for {pixelWidth}x{pixelHeight}"));
        _canvas = _surface.Canvas;
        _texture = SDL.CreateTexture(_renderer, SDL.PixelFormat.ARGB8888, SDL.TextureAccess.Streaming, pixelWidth, pixelHeight);
        if (_texture == 0)
        {
            throw new InvalidOperationException($"SDL_CreateTexture failed: {SDL.GetError()}");
        }

        _drawn = false;
    }

    public bool Begin(out SKCanvas canvas, out GRContext? context, out int age)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        context = null;
        canvas = _canvas!;
        age = _drawn ? 1 : 0;
        return _canvas is not null;
    }

    public void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _canvas!.Flush();
        _drawn = true;
        _ = SDL.UpdateTexture(_texture, 0, (nint)_pixels, _stride);
        _ = SDL.RenderTexture(_renderer, _texture, 0, 0);
        _ = SDL.RenderPresent(_renderer);
    }

    public bool ReadPixels(SKImageInfo info, nint pixels, int rowBytes) =>
        _surface is { } surface && surface.ReadPixels(info, pixels, rowBytes, 0, 0);

    public bool MakeCurrent() => true;

    private void ReleaseTarget()
    {
        if (_surface is { } surface)
        {
            SkiaCensus.Release(surface);
            _surface = null;
            _canvas = null;
        }

        if (_texture != 0)
        {
            SDL.DestroyTexture(_texture);
            _texture = 0;
        }

        if (_pixels is not null)
        {
            NativeMemory.Free(_pixels);
            _pixels = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseTarget();
        if (_renderer != 0)
        {
            SDL.DestroyRenderer(_renderer);
            _renderer = 0;
        }
    }
}
