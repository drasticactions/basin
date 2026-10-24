using Basin.Render.Skia;
using SDL3;
using SkiaSharp;

namespace Basin.Sdl;

/// <summary>
/// Draws into the <c>CAMetalLayer</c> of one window. Each frame wraps the texture of the next drawable and runs in an autorelease pool.
/// Drawables do not keep their contents, so the age is always 0.
/// </summary>
public sealed class SdlMetalLease : ISdlRenderLease
{
    private const ulong MtlPixelFormatBgra8Unorm = 80;
    private const ulong MtlPixelFormatRgba16Float = 115;

    private readonly SdlMetalDevice _device;
    private readonly SdlWindow _window;
    private readonly nint _view;
    private readonly nint _layer;
    private readonly nint _nextDrawable;
    private readonly nint _texture;
    private readonly nint _commandBuffer;
    private readonly nint _presentDrawable;
    private readonly nint _commit;
    private readonly nint _setDrawableSize;
    private nint _pool;
    private nint _drawable;
    private GRBackendRenderTarget? _target;
    private SKSurface? _surface;
    private int _width;
    private int _height;
    private bool _disposed;

    internal SdlMetalLease(SdlMetalDevice device, SdlWindow window)
    {
        _device = device;
        _window = window;
        _view = SDL.MetalCreateView(window.Handle);
        if (_view == 0)
        {
            throw new InvalidOperationException($"SDL_Metal_CreateView failed: {SDL.GetError()}");
        }

        _layer = SDL.MetalGetLayer(_view);
        _nextDrawable = SdlObjC.Selector("nextDrawable");
        _texture = SdlObjC.Selector("texture");
        _commandBuffer = SdlObjC.Selector("commandBuffer");
        _presentDrawable = SdlObjC.Selector("presentDrawable:");
        _commit = SdlObjC.Selector("commit");
        _setDrawableSize = SdlObjC.Selector("setDrawableSize:");
        SdlObjC.Send(_layer, SdlObjC.Selector("setDevice:"), device.Device);
        SdlObjC.SendULong(_layer, SdlObjC.Selector("setPixelFormat:"), MtlPixelFormatBgra8Unorm);
        SdlObjC.SendBool(_layer, SdlObjC.Selector("setFramebufferOnly:"), 0);
        SdlObjC.SendBool(_layer, SdlObjC.Selector("setDisplaySyncEnabled:"), 1);
        var (width, height) = window.PixelSize;
        Resize(width, height);
    }

    /// <inheritdoc/>
    public string Name => SdlRenderDevices.Metal;

    private SdlHdrState _hdr = SdlHdrState.Sdr;
    private SKColorType _colorType = SKColorType.Bgra8888;

    /// <inheritdoc/>
    public SdlHdrState Hdr => _hdr;

    /// <summary>
    /// Switches the layer between BGRA8 SDR and RGBA16F with the BT.2100 PQ color space for HDR.
    /// </summary>
    /// <param name="state">The requested state.</param>
    /// <returns><see langword="false"/> when HDR is requested and the system has no BT.2100 PQ color space.</returns>
    public bool TrySetHdr(in SdlHdrState state)
    {
        if (state.Enabled == _hdr.Enabled)
        {
            _hdr = state.Enabled ? state : SdlHdrState.Sdr;
            return true;
        }

        var space = state.Enabled ? SdlObjC.ColorSpaceNamed("kCGColorSpaceITUR_2100_PQ") : 0;
        if (state.Enabled && space == 0)
        {
            return false;
        }

        SdlObjC.SendBool(_layer, SdlObjC.Selector("setWantsExtendedDynamicRangeContent:"), state.Enabled ? (byte)1 : (byte)0);
        SdlObjC.SendULong(_layer, SdlObjC.Selector("setPixelFormat:"), state.Enabled ? MtlPixelFormatRgba16Float : MtlPixelFormatBgra8Unorm);
        SdlObjC.Send(_layer, SdlObjC.Selector("setColorspace:"), space);
        if (space != 0)
        {
            SdlObjC.CGColorSpaceRelease(space);
        }

        _colorType = state.Enabled ? SKColorType.RgbaF16 : SKColorType.Bgra8888;
        _hdr = state.Enabled ? state : SdlHdrState.Sdr;
        return true;
    }

    internal nint Layer => _layer;

    /// <inheritdoc/>
    public void Resize(int pixelWidth, int pixelHeight)
    {
        _width = Math.Max(1, pixelWidth);
        _height = Math.Max(1, pixelHeight);
        SdlObjC.SendSize(_layer, _setDrawableSize, new SdlObjC.CGSize { Width = _width, Height = _height });
    }

    /// <summary>
    /// Does nothing, because Metal has no current context.
    /// </summary>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool MakeCurrent() => true;

    /// <summary>
    /// Does not read the drawable back. The caller must render the frame offscreen instead.
    /// </summary>
    /// <param name="info">The size and format to read. It is not used.</param>
    /// <param name="pixels">The memory that receives the pixels. It is not used.</param>
    /// <param name="rowBytes">The stride of <paramref name="pixels"/> in bytes. It is not used.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool ReadPixels(SKImageInfo info, nint pixels, int rowBytes) => false;

    /// <summary>
    /// Takes the next drawable of the layer and starts a frame on its texture.
    /// </summary>
    /// <param name="canvas">The canvas over the drawable texture, in device pixels.</param>
    /// <param name="context">The shared Skia context of the device.</param>
    /// <param name="age">Always 0.</param>
    /// <returns><see langword="false"/> when the context is missing or lost, or the layer gives no drawable.</returns>
    public bool Begin(out SKCanvas canvas, out GRContext? context, out int age)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        canvas = null!;
        age = 0;
        context = _device.Context;
        if (context is null || context.IsAbandoned)
        {
            return false;
        }

        _pool = SdlObjC.objc_autoreleasePoolPush();
        _drawable = SdlObjC.Send(_layer, _nextDrawable);
        if (_drawable == 0)
        {
            End();
            return false;
        }

        var texture = SdlObjC.Send(_drawable, _texture);
        _target = SkiaCensus.Track(new GRBackendRenderTarget(_width, _height, new GRMtlTextureInfo(texture)));
        var surface = SKSurface.Create(context, _target, GRSurfaceOrigin.TopLeft, _colorType);
        if (surface is null)
        {
            End();
            return false;
        }

        _surface = SkiaCensus.Track(surface);
        canvas = surface.Canvas;
        return true;
    }

    /// <summary>
    /// Flushes Skia and presents the drawable from a command buffer on the device queue. Without a frame it only ends the autorelease pool.
    /// </summary>
    public void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_drawable == 0 || _device.Context is not { } context)
        {
            End();
            return;
        }

        context.Flush(submit: true, synchronous: false);
        var buffer = SdlObjC.Send(_device.Queue, _commandBuffer);
        SdlObjC.Send(buffer, _presentDrawable, _drawable);
        SdlObjC.Send(buffer, _commit);
        End();
    }

    private void End()
    {
        if (_surface is { } surface)
        {
            SkiaCensus.Release(surface);
            _surface = null;
        }

        if (_target is { } target)
        {
            SkiaCensus.Release(target);
            _target = null;
        }

        _drawable = 0;
        if (_pool != 0)
        {
            SdlObjC.objc_autoreleasePoolPop(_pool);
            _pool = 0;
        }
    }

    /// <summary>
    /// Ends a frame that is open and releases the Metal view. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        End();
        SDL.MetalDestroyView(_view);
    }
}
