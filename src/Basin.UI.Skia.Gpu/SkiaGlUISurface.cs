using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Basin.Render.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

public sealed class SkiaGlUISurface : ISkiaUISurface
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly PixmanRegion32 _wholeDamage = new();
    private readonly List<IBuffer> _retired = [];
    private readonly Dictionary<IBuffer, Entry> _wraps = [];
    private readonly GlDevice _device;
    private readonly IAllocator _allocator;
    private readonly GRContext _context;

    private IBuffer? _target;
    private IBuffer? _front;
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

    public UISurfaceSize Size
    {
        get
        {
            _thread.Assert();
            return new UISurfaceSize(_width, _height, _scale);
        }
    }

    private readonly UISurfaceObservers _observers = new();

    public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

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

        var allocated = _allocator.Allocate(physical.Width, physical.Height, DrmFormat.Argb8888, [], BufferUse.Render);
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
        _device.Gl.Flush();
        _drawing = false;
        _produced = true;
        if (ReferenceEquals(_target, _front))
        {
            _observers.Damaged(this, _wholeDamage);
        }
    }

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

    public bool AcceptsInputAt(double x, double y)
    {
        _thread.Assert();
        return !_disposed && x >= 0 && y >= 0 && x < _width && y < _height;
    }

    public string? CursorAt(double x, double y) => null;

    public void NotifyPointerEnter(double x, double y)
    {
    }

    public void NotifyPointerMotion(uint timeMs, double x, double y)
    {
    }

    public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
    {
    }

    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
    }

    public void NotifyPointerLeave()
    {
    }

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

    private Entry WrapOf(IBuffer buffer)
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

    private void Retire(IBuffer buffer)
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

    private static void DestroyBuffer(IBuffer buffer)
    {
        if (buffer is BufferBase concrete)
        {
            concrete.Destroy();
        }
    }

    private sealed record Entry(GlRenderTarget Native, GRBackendRenderTarget Backend, SKSurface Surface, SKCanvas Canvas);
}
