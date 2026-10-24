using System.Runtime.InteropServices;
using Basin.Render.Skia;
using SDL3;
using SkiaSharp;

namespace Basin.Sdl;

/// <summary>
/// Draws into a raster Skia surface over memory that the lease owns.
/// <see cref="Present"/> copies it into a streaming SDL texture and presents it with vsync.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlRasterLease"/> class with an SDL renderer for the window.
    /// </summary>
    /// <param name="window">The window to present into. The lease must be disposed before the window.</param>
    /// <exception cref="InvalidOperationException">SDL cannot create the renderer or the texture.</exception>
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

    /// <inheritdoc/>
    public string Name => SdlRenderDevices.Raster;

    /// <summary>
    /// Gets the name of the SDL renderer that presents the texture, or an empty string when SDL reports none.
    /// </summary>
    public string RendererName { get; }

    /// <inheritdoc/>
    public SdlHdrState Hdr => SdlHdrState.Sdr;

    /// <summary>
    /// Sets the size of the drawing target. A new size replaces the pixels and the texture, so the next frame has age 0.
    /// </summary>
    /// <param name="pixelWidth">The width in device pixels. A value below 1 counts as 1.</param>
    /// <param name="pixelHeight">The height in device pixels. A value below 1 counts as 1.</param>
    /// <exception cref="InvalidOperationException">Skia or SDL cannot create the new target.</exception>
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

    /// <summary>
    /// Starts a frame on the raster canvas. The lease keeps its pixels, so the age is 1 after the first present.
    /// </summary>
    /// <param name="canvas">The raster canvas, in device pixels.</param>
    /// <param name="context">Always <see langword="null"/>.</param>
    /// <param name="age">1 after the first present at the current size, else 0.</param>
    /// <returns><see langword="false"/> when the lease has no canvas.</returns>
    public bool Begin(out SKCanvas canvas, out GRContext? context, out int age)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        context = null;
        canvas = _canvas!;
        age = _drawn ? 1 : 0;
        return _canvas is not null;
    }

    /// <inheritdoc/>
    public void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _canvas!.Flush();
        _drawn = true;
        _ = SDL.UpdateTexture(_texture, 0, (nint)_pixels, _stride);
        _ = SDL.RenderTexture(_renderer, _texture, 0, 0);
        _ = SDL.RenderPresent(_renderer);
    }

    /// <inheritdoc/>
    public bool ReadPixels(SKImageInfo info, nint pixels, int rowBytes) =>
        _surface is { } surface && surface.ReadPixels(info, pixels, rowBytes, 0, 0);

    /// <summary>
    /// Does nothing, because a raster lease has no context.
    /// </summary>
    /// <returns>Always <see langword="true"/>.</returns>
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

    /// <summary>
    /// Frees the pixels, the texture and the SDL renderer. A second call does nothing.
    /// </summary>
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
