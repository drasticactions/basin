using Basin.Render.Skia;
using SDL3;
using SkiaSharp;

namespace Basin.Sdl;

/// <summary>
/// Draws into the default framebuffer of one GL window with the shared context of an <see cref="SdlGlDevice"/>.
/// The age comes from <c>EGL_EXT_buffer_age</c> when the display offers it, else it is 0.
/// </summary>
public sealed class SdlGlLease : ISdlRenderLease
{
    private const uint GlRgba8 = 0x8058;

    private readonly SdlGlDevice _device;
    private readonly SdlWindow _window;
    private readonly nint _eglSurface;
    private GRBackendRenderTarget? _target;
    private SKSurface? _surface;
    private SKCanvas? _canvas;
    private int _width;
    private int _height;
    private bool _disposed;

    internal SdlGlLease(SdlGlDevice device, SdlWindow window)
    {
        _device = device;
        _window = window;
        if (device.HasBufferAge)
        {
            _ = device.MakeCurrent(window);
            _eglSurface = SDL.EGLGetWindowSurface(window.Handle);
        }

        var (width, height) = window.PixelSize;
        Resize(width, height);
    }

    /// <inheritdoc/>
    public string Name => SdlRenderDevices.Gl;

    /// <inheritdoc/>
    public SdlHdrState Hdr => SdlHdrState.Sdr;

    /// <inheritdoc/>
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
    }

    private bool EnsureTarget()
    {
        if (_surface is not null)
        {
            return true;
        }

        if (_device.Context is not { } context)
        {
            return false;
        }

        _target = SkiaCensus.Track(new GRBackendRenderTarget(
            _width, _height, sampleCount: 0, stencilBits: 8, new GRGlFramebufferInfo(0, GlRgba8)));
        var surface = SKSurface.Create(context, _target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        if (surface is null)
        {
            SkiaCensus.Release(_target);
            _target = null;
            return false;
        }

        _surface = SkiaCensus.Track(surface);
        _canvas = surface.Canvas;
        return true;
    }

    /// <summary>
    /// Makes the shared context current on the window, resets the Skia GL state and starts a frame.
    /// </summary>
    /// <param name="canvas">The canvas over framebuffer 0, in device pixels.</param>
    /// <param name="context">The shared Skia context of the device.</param>
    /// <param name="age">The EGL buffer age, or 0 when it is unknown.</param>
    /// <returns><see langword="false"/> when the context is missing or lost, or cannot be made current.</returns>
    public bool Begin(out SKCanvas canvas, out GRContext? context, out int age)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        canvas = null!;
        context = _device.Context;
        age = 0;
        if (context is null || context.IsAbandoned || !_device.MakeCurrent(_window))
        {
            return false;
        }

        context.ResetContext();
        if (!EnsureTarget())
        {
            return false;
        }

        if (_eglSurface != 0)
        {
            age = SdlEgl.BufferAge(_device.EglDisplay, _eglSurface);
        }

        canvas = _canvas!;
        return true;
    }

    /// <inheritdoc/>
    public void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device.Context?.Flush();
        _ = SDL.GLSwapWindow(_window.Handle);
    }

    /// <inheritdoc/>
    public bool ReadPixels(SKImageInfo info, nint pixels, int rowBytes) =>
        _surface is { } surface && surface.ReadPixels(info, pixels, rowBytes, 0, 0);

    /// <inheritdoc/>
    public bool MakeCurrent() => _device.Context is not null && _device.MakeCurrent(_window);

    private void ReleaseTarget()
    {
        if (_surface is { } surface)
        {
            SkiaCensus.Release(surface);
            _surface = null;
            _canvas = null;
        }

        if (_target is { } target)
        {
            SkiaCensus.Release(target);
            _target = null;
        }
    }

    /// <summary>
    /// Releases the Skia surface over the window. The device keeps the shared context. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_device.Context is not null)
        {
            _ = _device.MakeCurrent(_window);
        }

        ReleaseTarget();
    }
}
