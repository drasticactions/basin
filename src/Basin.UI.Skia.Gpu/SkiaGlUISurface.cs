using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Basin.Render.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

/// <summary>
/// A Skia surface that Ganesh draws on a GL device into an Argb8888 dmabuf. Each frame publishes a write fence on the dmabuf. Use it on the thread that created it.
/// </summary>
public sealed class SkiaGlUISurface : ISkiaUISurface
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly PixmanRegion32 _wholeDamage = new();
    private readonly List<BufferBase> _retired = [];
    private readonly Dictionary<BufferBase, Entry> _wraps = [];
    private readonly GlDevice _device;
    private readonly IAllocator _allocator;
    private readonly GRContext _context;

    private BufferBase? _target;
    private BufferBase? _front;
    private int _width;
    private int _height;
    private double _scale;
    private bool _drawing;
    private bool _produced;
    private bool _disposed;

    internal SkiaGlUISurface(GlDevice device, IAllocator allocator, GRContext context)
    {
        _device = device;
        _allocator = allocator;
        _context = context;
    }

    /// <inheritdoc/>
    public UISurfaceSize Size
    {
        get
        {
            _thread.Assert();
            return new UISurfaceSize(_width, _height, _scale);
        }
    }

    private readonly UISurfaceObservers _observers = new();

    /// <inheritdoc/>
    public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Sets the logical size and the scale for the next frame. A change allocates a new dmabuf buffer, and the frame on screen stays valid until the next <see cref="TryAcquire"/>.
    /// </summary>
    /// <param name="logicalWidth">The width in logical pixels. It must be positive.</param>
    /// <param name="logicalHeight">The height in logical pixels. It must be positive.</param>
    /// <param name="scale">The scale from logical to buffer pixels. The surface snaps it.</param>
    /// <returns><see langword="false"/> when a value is not valid, the surface is disposed or drawing, or the allocation fails.</returns>
    public bool Configure(int logicalWidth, int logicalHeight, double scale)
    {
        _thread.Assert();
        if (_disposed || _drawing || logicalWidth <= 0 || logicalHeight <= 0 || scale <= 0)
        {
            return false;
        }

        scale = OutputScaling.Snap(scale);
        var sizeUnchanged = logicalWidth == _width && logicalHeight == _height && scale == _scale;
        if (sizeUnchanged && _target is not null && !ReferenceEquals(_target, _front))
        {
            return true;
        }

        var physical = OutputScaling.ToPhysical(new Box(0, 0, logicalWidth, logicalHeight), scale);
        if (physical.IsEmpty)
        {
            return false;
        }

        var allocated = _allocator.Allocate(
            physical.Width, physical.Height, DrmFormat.Argb8888, [], BufferUse.Render | BufferUse.Scanout);
        if (allocated is null)
        {
            return false;
        }

        if (_target is not null && !ReferenceEquals(_target, _front))
        {
            Retire(_target);
        }

        _target = allocated;
        _produced = false;
        (_width, _height, _scale) = (logicalWidth, logicalHeight, scale);
        return true;
    }

    /// <summary>
    /// Starts a GPU frame in the buffer that the next <see cref="TryAcquire"/> shows. The canvas keeps the last pixels and is scaled to logical pixels.
    /// </summary>
    /// <returns>The canvas of the frame. It is valid only until <see cref="EndDraw"/>.</returns>
    /// <exception cref="ObjectDisposedException">The surface is disposed.</exception>
    /// <exception cref="InvalidOperationException">A draw is in progress, the Ganesh context is abandoned, <see cref="Configure"/> has not succeeded, or Ganesh rejects the buffer.</exception>
    public SKCanvas BeginDraw()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_drawing)
        {
            throw new InvalidOperationException("BeginDraw without EndDraw.");
        }

        if (_context.IsAbandoned)
        {
            throw new InvalidOperationException("The Ganesh context is abandoned.");
        }

        var target = _target ?? throw new InvalidOperationException("BeginDraw before Configure.");
        var entry = WrapOf(target);

        _context.ResetContext(uint.MaxValue);
        _drawing = true;
        var canvas = entry.Canvas;
        canvas.Save();
        canvas.Scale(target.Width / (float)_width, target.Height / (float)_height);
        return canvas;
    }

    /// <summary>
    /// Ends the frame, flushes Ganesh and publishes a write fence on the dmabuf. Without a fence, it waits for the GPU. When the frame went into the buffer that is on screen, the observers get damage for the whole surface.
    /// </summary>
    /// <exception cref="InvalidOperationException">No draw is in progress.</exception>
    public void EndDraw()
    {
        _thread.Assert();
        if (!_drawing)
        {
            throw new InvalidOperationException("EndDraw without BeginDraw.");
        }

        var entry = _wraps[_target!];
        entry.Canvas.Restore();
        _context.Flush(submit: true, synchronous: false);
        PublishWriteFence();
        _drawing = false;
        _produced = true;
        if (ReferenceEquals(_target, _front))
        {
            _observers.Damaged(this, _wholeDamage);
        }
    }

    private void PublishWriteFence()
    {
        var fence = _target!.TryGetDmabuf(out var attributes) ? _device.ExportFence() : -1;
        if (fence < 0)
        {
            _device.Gl.Finish();
            return;
        }

        RenderFences.PublishFenceTo(attributes, forWrite: true, fence);
        RenderFences.CloseFence(fence);
    }

    /// <summary>
    /// Takes the last drawn frame for display. The frame carries no damage, so the whole buffer counts as changed.
    /// </summary>
    /// <param name="frame">The frame. The caller must dispose it to release the buffer lock.</param>
    /// <returns><see langword="false"/> when the surface is disposed or has not drawn since the last <see cref="Configure"/> that changed the size.</returns>
    public bool TryAcquire(out UIFrame frame)
    {
        _thread.Assert();
        if (_disposed || !_produced || _target is null)
        {
            frame = default;
            return false;
        }

        if (_front is not null && !ReferenceEquals(_front, _target))
        {
            Retire(_front);
        }

        _front = _target;
        frame = new UIFrame(_front.Lock(), damage: null);
        return true;
    }

    /// <summary>
    /// Finds whether a point is inside the surface. Every point inside takes input.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <returns><see langword="true"/> when the point is inside the logical size and the surface is live.</returns>
    public bool AcceptsInputAt(double x, double y)
    {
        _thread.Assert();
        return !_disposed && x >= 0 && y >= 0 && x < _width && y < _height;
    }

    /// <inheritdoc/>
    public ISkiaPointerHandler? Pointer { get; set; }

    /// <summary>
    /// Asks <see cref="Pointer"/> for the cursor at a point.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <returns>An xcursor name, or <see langword="null"/> when there is no handler or it has no preference.</returns>
    public string? CursorAt(double x, double y) => Pointer?.CursorAt(x, y);

    /// <inheritdoc/>
    public void NotifyPointerEnter(double x, double y)
    {
        Pointer?.PointerEnter(x, y);
    }

    /// <inheritdoc/>
    public void NotifyPointerMotion(uint timeMs, double x, double y)
    {
        Pointer?.PointerMotion(timeMs, x, y);
    }

    /// <inheritdoc/>
    public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
    {
        Pointer?.PointerButton(timeMs, button, pressed);
    }

    /// <inheritdoc/>
    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
        Pointer?.PointerAxis(timeMs, dx, dy);
    }

    /// <inheritdoc/>
    public void NotifyPointerLeave()
    {
        Pointer?.PointerLeave();
    }

    /// <summary>
    /// Creates a CPU <see cref="SkiaUISurface"/> with the size of the anchor box and the scale of this surface. The caller owns the popup and places it.
    /// </summary>
    /// <param name="anchor">The anchor box in surface-local logical coordinates. An empty side becomes 1.</param>
    /// <param name="gravity">The popup direction. The surface ignores it.</param>
    /// <returns>The popup, or <see langword="null"/> when this surface is disposed or not configured.</returns>
    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity)
    {
        _thread.Assert();
        if (_disposed)
        {
            return null;
        }

        var popup = new SkiaUISurface();
        if (!popup.Configure(Math.Max(1, anchor.Width), Math.Max(1, anchor.Height), _scale))
        {
            popup.Dispose();
            return null;
        }

        return popup;
    }

    /// <summary>
    /// Ends a draw in progress, releases the GPU targets, destroys the buffers and tells the observers. A buffer that a frame still locks is destroyed too.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        if (_drawing)
        {
            _wraps[_target!].Canvas.Restore();
            _drawing = false;
        }

        _disposed = true;
        foreach (var (buffer, entry) in _wraps)
        {
            ReleaseEntry(entry);
            _ = buffer;
        }

        _wraps.Clear();
        if (_target is not null && !ReferenceEquals(_target, _front) && !_target.IsDestroyed)
        {
            DestroyBuffer(_target);
        }

        if (_front is not null && !_front.IsDestroyed)
        {
            DestroyBuffer(_front);
        }

        foreach (var buffer in _retired.ToArray())
        {
            if (!buffer.IsDestroyed)
            {
                DestroyBuffer(buffer);
            }
        }

        _retired.Clear();
        _target = null;
        _front = null;
        _wholeDamage.Dispose();
        _observers.Destroyed(this);
    }

    private Entry WrapOf(BufferBase buffer)
    {
        if (_wraps.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        var native = GlRenderTarget.Create(_device, buffer);
        var backend = SkiaCensus.Track(new GRBackendRenderTarget(
            buffer.Width, buffer.Height, sampleCount: 0, stencilBits: 0,
            new GRGlFramebufferInfo(native.Framebuffer, 0x93A1)));
        var surface = SKSurface.Create(_context, backend, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888);
        if (surface is null)
        {
            SkiaCensus.Release(backend);
            native.Dispose(_device);
            throw new InvalidOperationException("Ganesh rejected the frame buffer's framebuffer.");
        }

        var entry = new Entry(native, backend, SkiaCensus.Track(surface), surface.Canvas);
        _wraps[buffer] = entry;
        return entry;
    }

    private void Retire(BufferBase buffer)
    {
        if (_wraps.Remove(buffer, out var entry))
        {
            ReleaseEntry(entry);
        }

        if (buffer.LockCount == 0)
        {
            DestroyBuffer(buffer);
            return;
        }

        _retired.Add(buffer);
        buffer.Released += () =>
        {
            if (_retired.Remove(buffer) && !buffer.IsDestroyed)
            {
                DestroyBuffer(buffer);
            }
        };
    }

    private void ReleaseEntry(Entry entry)
    {
        SkiaCensus.Release(entry.Surface);
        SkiaCensus.Release(entry.Backend);
        if (!_context.IsAbandoned)
        {
            entry.Native.Dispose(_device);
        }
    }

    private static void DestroyBuffer(BufferBase buffer) => buffer.Destroy();

    private sealed record Entry(GlRenderTarget Native, GRBackendRenderTarget Backend, SKSurface Surface, SKCanvas Canvas);
}
