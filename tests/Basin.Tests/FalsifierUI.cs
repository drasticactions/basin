using Basin.Capabilities;
using Basin.UI.Decoration;
using Pixman;

namespace Basin.Tests;

internal sealed class FalsifierUIHost : IUIHost
{
    public UITargetKind Produces => UITargetKind.Memory;

    public long? NextDueMillis => null;

    public event Action? WakeupRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    public IUISurface? CreateSurface(in UISurfaceOptions options)
    {
        if (options.Target != UITargetKind.Memory)
        {
            return null;
        }

        var surface = new FalsifierUISurface();
        if (!surface.Configure(options.Width, options.Height, options.Scale))
        {
            surface.Dispose();
            return null;
        }

        return surface;
    }

    public void Pump()
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class FalsifierUISurface : IUISurface
{
    private readonly PixmanRegion32 _whole = new();
    private readonly List<MemoryBuffer> _retired = [];
    private MemoryBuffer? _target;
    private MemoryBuffer? _front;
    private int _width;
    private int _height;
    private double _scale;
    private bool _produced;
    private bool _disposed;

    public UISurfaceSize Size => new(_width, _height, _scale);

    private readonly UISurfaceObservers _observers = new();

    public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

    public bool Configure(int logicalWidth, int logicalHeight, double scale)
    {
        if (_disposed || logicalWidth <= 0 || logicalHeight <= 0 || scale <= 0)
        {
            return false;
        }

        scale = OutputScaling.Snap(scale);
        if (logicalWidth == _width && logicalHeight == _height && scale == _scale &&
            _target is not null && !ReferenceEquals(_target, _front))
        {
            return true;
        }

        var physical = OutputScaling.ToPhysical(new Box(0, 0, logicalWidth, logicalHeight), scale);
        if (_target is not null && !ReferenceEquals(_target, _front))
        {
            _target.Destroy();
        }

        _target = new MemoryBuffer(physical.Width, physical.Height, DrmFormat.Argb8888);
        _produced = false;
        (_width, _height, _scale) = (logicalWidth, logicalHeight, scale);
        return true;
    }

    public BufferDataView BeginPixels()
    {
        if (!_target!.BeginDataAccess(BufferDataAccess.Read | BufferDataAccess.Write, out var view))
        {
            throw new InvalidOperationException("no CPU path");
        }

        return view;
    }

    public void EndPixels()
    {
        _target!.EndDataAccess();
        _produced = true;
        if (ReferenceEquals(_target, _front))
        {
            _observers.Damaged(this, _whole);
        }
    }

    public bool TryAcquire(out UIFrame frame)
    {
        if (_disposed || !_produced || _target is null)
        {
            frame = default;
            return false;
        }

        if (_front is not null && !ReferenceEquals(_front, _target))
        {
            var replaced = _front;
            if (replaced.LockCount == 0)
            {
                replaced.Destroy();
            }
            else
            {
                _retired.Add(replaced);
                replaced.Released += () =>
                {
                    if (_retired.Remove(replaced) && !replaced.IsDestroyed)
                    {
                        replaced.Destroy();
                    }
                };
            }
        }

        _front = _target;
        frame = new UIFrame(_front.Lock(), damage: null);
        return true;
    }

    public bool AcceptsInputAt(double x, double y) => !_disposed && x >= 0 && y >= 0 && x < _width && y < _height;

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

    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
        _whole.Dispose();
        _observers.Destroyed(this);
    }
}

internal sealed class FalsifierFrameRenderer : IFrameRenderer
{
    private const int Border = 4;
    private const int TitleHeight = 30;
    private const int CloseSide = 16;

    private int _outerWidth;
    private int _outerHeight;

    public FrameInsets Measure(in FrameState state, double scale) =>
        new(Border + TitleHeight, Border, Border, Border);

    public unsafe void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction)
    {
        var falsifier = (FalsifierUISurface)surface;
        _outerWidth = clientBox.Width + 2 * Border;
        _outerHeight = clientBox.Height + TitleHeight + 2 * Border;
        var scale = falsifier.Size.Scale;
        var chrome = state.Active ? 0xFF206040u : 0xFF404040u;

        var view = falsifier.BeginPixels();
        try
        {
            var physical = OutputScaling.ToPhysical(new Box(0, 0, _outerWidth, _outerHeight), scale);
            var hole = OutputScaling.ToPhysical(clientBox, scale);
            var close = OutputScaling.ToPhysical(CloseBox(), scale);
            for (var y = 0; y < physical.Height; y++)
            {
                var row = (uint*)(view.Data + y * view.Stride);
                for (var x = 0; x < physical.Width; x++)
                {
                    var inHole = x >= hole.X && x < hole.Right && y >= hole.Y && y < hole.Bottom;
                    var inClose = x >= close.X && x < close.Right && y >= close.Y && y < close.Bottom;
                    row[x] = inHole ? 0u : inClose ? 0xFFB03030u : chrome;
                }
            }
        }
        finally
        {
            falsifier.EndPixels();
        }
    }

    public FramePart PartAt(double x, double y, in FrameState state, double scale)
    {
        var w = _outerWidth;
        var h = _outerHeight;
        if (w <= 0 || x < 0 || y < 0 || x >= w || y >= h)
        {
            return FramePart.None;
        }

        if (x >= w - Border && y >= h - Border)
        {
            return FramePart.BottomRight;
        }

        if (x < Border)
        {
            return FramePart.Left;
        }

        if (x >= w - Border)
        {
            return FramePart.Right;
        }

        if (y >= h - Border)
        {
            return FramePart.Bottom;
        }

        if (y < Border + TitleHeight)
        {
            var close = CloseBox();
            if (x >= close.X && x < close.Right && y >= close.Y && y < close.Bottom)
            {
                return FramePart.Close;
            }

            return y < Border ? FramePart.Top : FramePart.Title;
        }

        return FramePart.Border;
    }

    public Box PartBounds(FramePart part) => new(0, 0, _outerWidth, _outerHeight);

    private Box CloseBox() =>
        new(_outerWidth - Border - 4 - CloseSide, Border + (TitleHeight - CloseSide) / 2, CloseSide, CloseSide);
}
