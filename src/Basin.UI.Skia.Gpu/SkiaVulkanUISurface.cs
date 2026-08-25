using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using Pixman;
using SkiaSharp;
using static Basin.UI.Skia.SkiaUILog;

namespace Basin.UI.Skia;

public sealed class SkiaVulkanUISurface : ISkiaUISurface
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly PixmanRegion32 _wholeDamage = new();
    private readonly List<IBuffer> _retired = [];
    private readonly VulkanDevice _device;
    private readonly Func<IBuffer, IVulkanDrawTarget> _wrapFactory;
    private readonly Action _flush;
    private readonly IAllocator? _allocator;
    private readonly ulong[] _modifiers = [];

    private IBuffer? _target;
    private IBuffer? _front;
    private IVulkanDrawTarget? _wrap;
    private IBuffer? _wrapped;
    private int _width;
    private int _height;
    private double _scale;
    private bool _drawing;
    private bool _produced;
    private bool _disposed;

    internal SkiaVulkanUISurface(
        VulkanDevice device,
        Func<IBuffer, IVulkanDrawTarget> wrapFactory,
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

        if (_target is not null && !ReferenceEquals(_target, _front))
        {
            Retire(_target);
        }

        var allocated = _allocator is null
            ? new MemoryBuffer(physical.Width, physical.Height, DrmFormat.Argb8888)
            : _allocator.Allocate(physical.Width, physical.Height, DrmFormat.Argb8888, _modifiers, BufferUse.Render);
        if (allocated is null)
        {
            return false;
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

    private void Retire(IBuffer buffer)
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

    private static void Destroy(IBuffer buffer)
    {
        if (buffer is BufferBase owned && !owned.IsDestroyed)
        {
            owned.Destroy();
        }
    }
}
