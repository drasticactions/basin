using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

public sealed class SkiaUISurface : ISkiaUISurface
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    private readonly PixmanRegion32 _wholeDamage = new();

    private readonly List<MemoryBuffer> _retired = [];
    private MemoryBuffer? _target;
    private MemoryBuffer? _front;
    private SKSurface? _wrap;
    private SKCanvas? _wrapCanvas;
    private MemoryBuffer? _wrapBuffer;
    private nint _wrapData;
    private int _width;
    private int _height;
    private double _scale;
    private bool _drawing;
    private bool _produced;
    private bool _disposed;

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

        _target = new MemoryBuffer(physical.Width, physical.Height, DrmFormat.Argb8888);
        _produced = false;
        _width = logicalWidth;
        _height = logicalHeight;
        _scale = scale;
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
        if (!target.BeginDataAccess(BufferDataAccess.Read | BufferDataAccess.Write, out var view))
        {
            throw new InvalidOperationException("The surface's buffer has no CPU path.");
        }

        if (_wrap is null || !ReferenceEquals(_wrapBuffer, target) || _wrapData != view.Data)
        {
            ReleaseWrap();
            if (!SkiaRenderer.TryImageInfo(target.Width, target.Height, DrmFormat.Argb8888, out var info))
            {
                target.EndDataAccess();
                throw new InvalidOperationException("No Skia pixel layout for the surface format.");
            }

            var wrap = SKSurface.Create(info, view.Data, view.Stride);
            if (wrap is null)
            {
                target.EndDataAccess();
                throw new InvalidOperationException("Skia rejected the surface's pixel layout.");
            }

            _wrap = SkiaCensus.Track(wrap);
            _wrapCanvas = wrap.Canvas;
            _wrapBuffer = target;
            _wrapData = view.Data;
        }

        _drawing = true;
        var canvas = _wrapCanvas!;
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

        _wrapCanvas!.Restore();
        _target!.EndDataAccess();
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
            _wrapCanvas?.Restore();
            _target?.EndDataAccess();
            _drawing = false;
        }

        _disposed = true;
        ReleaseWrap();
        if (_target is not null && !ReferenceEquals(_target, _front) && !_target.IsDestroyed)
        {
            _target.Destroy();
        }

        if (_front is not null && !_front.IsDestroyed)
        {
            _front.Destroy();
        }

        foreach (var buffer in _retired.ToArray())
        {
            if (!buffer.IsDestroyed)
            {
                buffer.Destroy();
            }
        }

        _retired.Clear();
        _target = null;
        _front = null;
        _wholeDamage.Dispose();
        _observers.Destroyed(this);
    }

    private void Retire(MemoryBuffer buffer)
    {
        ReleaseWrapFor(buffer);
        if (buffer.LockCount == 0)
        {
            buffer.Destroy();
            return;
        }

        _retired.Add(buffer);
        buffer.Released += () =>
        {
            if (_retired.Remove(buffer) && !buffer.IsDestroyed)
            {
                buffer.Destroy();
            }
        };
    }

    private void ReleaseWrapFor(MemoryBuffer buffer)
    {
        if (ReferenceEquals(_wrapBuffer, buffer))
        {
            ReleaseWrap();
        }
    }

    private void ReleaseWrap()
    {
        if (_wrap is not null)
        {
            SkiaCensus.Release(_wrap);
            _wrap = null;
            _wrapCanvas = null;
            _wrapBuffer = null;
            _wrapData = 0;
        }
    }
}
