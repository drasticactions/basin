using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using Pixman;
using SkiaSharp;
using static Basin.UI.Skia.SkiaUILog;

namespace Basin.UI.Skia;

/// <summary>
/// A Skia surface that Ganesh or Graphite draws on a Vulkan device, into an Argb8888 dmabuf or a memory buffer. Use it on the thread that created it.
/// </summary>
public sealed class SkiaVulkanUISurface : ISkiaUISurface
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly PixmanRegion32 _wholeDamage = new();
    private readonly List<BufferBase> _retired = [];
    private readonly VulkanDevice _device;
    private readonly Func<BufferBase, IVulkanDrawTarget> _wrapFactory;
    private readonly Action _flush;
    private readonly IAllocator? _allocator;
    private readonly ulong[] _modifiers = [];

    private BufferBase? _target;
    private BufferBase? _front;
    private IVulkanDrawTarget? _wrap;
    private BufferBase? _wrapped;
    private int _width;
    private int _height;
    private double _scale;
    private bool _drawing;
    private bool _produced;
    private bool _disposed;

    internal SkiaVulkanUISurface(
        VulkanDevice device,
        Func<BufferBase, IVulkanDrawTarget> wrapFactory,
        Action flush,
        IAllocator? allocator,
        ulong[] modifiers)
    {
        _device = device;
        _wrapFactory = wrapFactory;
        _flush = flush;
        _allocator = allocator;
        _modifiers = modifiers;
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
    /// Sets the logical size and the scale for the next frame. A change allocates a new dmabuf or memory buffer, and the frame on screen stays valid until the next <see cref="TryAcquire"/>.
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

        if (_target is not null && !ReferenceEquals(_target, _front))
        {
            Retire(_target);
        }

        var allocated = _allocator is null
            ? new MemoryBuffer(physical.Width, physical.Height, DrmFormat.Argb8888)
            : _allocator.Allocate(
                physical.Width, physical.Height, DrmFormat.Argb8888, _modifiers,
                BufferUse.Render | BufferUse.Scanout);
        if (allocated is null)
        {
            return false;
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
    /// <exception cref="InvalidOperationException">A draw is in progress, or <see cref="Configure"/> has not succeeded.</exception>
    public SKCanvas BeginDraw()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_drawing)
        {
            throw new InvalidOperationException("BeginDraw without EndDraw.");
        }

        var target = _target ?? throw new InvalidOperationException("BeginDraw before Configure.");

        if (_wrap is null || !ReferenceEquals(_wrapped, target))
        {
            _wrap?.Dispose();
            _wrap = _wrapFactory(target);
            _wrapped = target;
            if (_allocator is not null && _wrap.IsCpuReadback)
            {
                Log.Warn($"skia-vulkan ui: the device refused to render into its own dmabuf ({target.Width}x{target.Height}); chrome will not draw");
            }
        }

        _drawing = true;
        var canvas = _wrap.Canvas;
        canvas.Save();
        canvas.Scale(target.Width / (float)_width, target.Height / (float)_height);
        return canvas;
    }

    /// <summary>
    /// Ends the frame and submits it. A dmabuf frame gets a write fence, or the device waits for idle. A memory frame is read back from the GPU. When the frame went into the buffer that is on screen, the observers get damage for the whole surface.
    /// </summary>
    /// <exception cref="InvalidOperationException">No draw is in progress.</exception>
    public void EndDraw()
    {
        _thread.Assert();
        if (!_drawing)
        {
            throw new InvalidOperationException("EndDraw without BeginDraw.");
        }

        var wrap = _wrap!;
        wrap.Canvas.Restore();
        _flush();
        if (wrap.IsCpuReadback)
        {
            wrap.ReadInto((MemoryBuffer)_target!);
        }
        else if (!_target!.TryGetDmabuf(out var attributes) || !_device.PublishWriteFence(attributes, wrap.Imported))
        {
            _device.Api.DeviceWaitIdle(_device.Device);
        }

        _drawing = false;
        _produced = true;
        if (ReferenceEquals(_target, _front))
        {
            _observers.Damaged(this, _wholeDamage);
        }
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
            _wrap?.Canvas.Restore();
            _drawing = false;
        }

        _disposed = true;
        _wrap?.Dispose();
        _wrap = null;
        _wrapped = null;
        if (_target is not null && !ReferenceEquals(_target, _front))
        {
            Destroy(_target);
        }

        if (_front is not null)
        {
            Destroy(_front);
        }

        foreach (var buffer in _retired.ToArray())
        {
            Destroy(buffer);
        }

        _retired.Clear();
        _target = null;
        _front = null;
        _wholeDamage.Dispose();
        _observers.Destroyed(this);
    }

    private void Retire(BufferBase buffer)
    {
        if (buffer.LockCount == 0)
        {
            Destroy(buffer);
            return;
        }

        _retired.Add(buffer);
        buffer.Released += () =>
        {
            if (_retired.Remove(buffer))
            {
                Destroy(buffer);
            }
        };
    }

    private static void Destroy(BufferBase buffer) => buffer.Destroy();
}
