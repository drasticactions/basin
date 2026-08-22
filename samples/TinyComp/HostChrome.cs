using Basin;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Microsoft.Extensions.Logging;

namespace TinyComp;

internal sealed class HostChrome : IDisposable
{
    private const uint BtnLeft = 0x110;
    private const uint DoubleClickMillis = 400;

    private readonly TinyComp _comp;
    private readonly WaylandHostFrame _frame;
    private readonly IFrameRenderer _renderer;
    private readonly Action _onClose;
    private readonly string _title;

    private IUISurface? _surface;
    private FramePart _hot = FramePart.None;
    private FramePart _pressed = FramePart.None;
    private double _pointerX;
    private double _pointerY;
    private bool _pointerIn;
    private uint _lastTitlePressMs;
    private int _shownOuterWidth;
    private int _shownOuterHeight;
    private double _shownScale;
    private bool _faulted;
    private bool _disposed;

    internal HostChrome(TinyComp comp, WaylandHostFrame frame, IFrameRenderer renderer, string title, Action onClose)
    {
        _comp = comp;
        _frame = frame;
        _renderer = renderer;
        _title = title;
        _onClose = onClose;

        _frame.StateChanged += OnStateChanged;
        _frame.PointerEnter += OnPointerEnter;
        _frame.PointerMotion += OnPointerMotion;
        _frame.PointerButton += OnPointerButton;
        _frame.PointerLeave += OnPointerLeave;
        Relayout();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frame.StateChanged -= OnStateChanged;
        _frame.PointerEnter -= OnPointerEnter;
        _frame.PointerMotion -= OnPointerMotion;
        _frame.PointerButton -= OnPointerButton;
        _frame.PointerLeave -= OnPointerLeave;
        _surface?.Dispose();
        _surface = null;
    }

    internal void OnOutputChanged(OutputStateFields fields)
    {
        if (_frame.OuterWidth == _shownOuterWidth &&
            _frame.OuterHeight == _shownOuterHeight &&
            _frame.Scale == _shownScale)
        {
            return;
        }

        Relayout();
    }

    private void Relayout()
    {
        if (_disposed || _faulted)
        {
            return;
        }

        var state = BuildState();
        var scale = _frame.Scale;
        var insets = _renderer.Measure(state, scale);
        if (!_frame.SetInsets(new HostFrameInsets(insets.Top, insets.Right, insets.Bottom, insets.Left)))
        {
            return;
        }

        Repaint(state);
    }

    private void Repaint(in FrameState state)
    {
        if (_disposed || _faulted)
        {
            return;
        }

        var scale = _frame.Scale;
        var outerWidth = _frame.OuterWidth;
        var outerHeight = _frame.OuterHeight;
        var insets = _frame.Insets;
        if (outerWidth <= 0 || outerHeight <= 0 || insets.IsEmpty)
        {
            return;
        }

        try
        {
            if (!EnsureSurface(outerWidth, outerHeight, scale))
            {
                return;
            }

            var hole = new Box(
                insets.Left,
                insets.Top,
                outerWidth - insets.Left - insets.Right,
                outerHeight - insets.Top - insets.Bottom);
            _renderer.Draw(_surface!, hole, state, new FrameInteraction(_hot, _pressed));
            if (!_surface!.TryAcquire(out var produced))
            {
                return;
            }

            using (produced)
            {
                if (produced.Buffer is { } buffer)
                {
                    _frame.Attach(buffer, produced.Damage);
                }
            }

            _shownOuterWidth = outerWidth;
            _shownOuterHeight = outerHeight;
            _shownScale = scale;
        }
        catch (Exception e)
        {
            _faulted = true;
            _frame.SetInsets(default);
            _comp.Log.LogError("host frame fault: {Reason}", e.Message);
        }
    }

    private bool EnsureSurface(int outerWidth, int outerHeight, double scale)
    {
        if (_surface is null)
        {
            var target = (_comp.UIHost.Produces & UITargetKind.Dmabuf) != 0
                ? UITargetKind.Dmabuf
                : UITargetKind.Memory;
            _surface = _comp.UIHost.CreateSurface(new UISurfaceOptions
            {
                Target = target,
                Width = outerWidth,
                Height = outerHeight,
                Scale = scale,
            });
            return _surface is not null;
        }

        return _surface.Configure(outerWidth, outerHeight, scale);
    }

    private FrameState BuildState() => new()
    {
        Title = _title,
        AppId = "dev.basin.compositor",
        Active = _frame.Activated,
        Maximized = _frame.Maximized,
        Fullscreen = _frame.Fullscreen,
        Resizing = _frame.Resizing,
        Capabilities = FrameCapabilities.Maximize | FrameCapabilities.Minimize,
    };

    private void OnStateChanged() => Relayout();

    private void OnPointerEnter(double x, double y)
    {
        _pointerIn = true;
        UpdateHover(x, y);
    }

    private void OnPointerMotion(uint timeMs, double x, double y) => UpdateHover(x, y);

    private void OnPointerLeave()
    {
        _pointerIn = false;
        if (_hot == FramePart.None && _pressed == FramePart.None)
        {
            return;
        }

        _hot = FramePart.None;

        _pressed = FramePart.None;
        Repaint(BuildState());
    }

    private void UpdateHover(double x, double y)
    {
        _pointerX = x;
        _pointerY = y;
        var part = _renderer.PartAt(x, y, BuildState(), _frame.Scale);
        if (part == _hot)
        {
            return;
        }

        _hot = part;
        _comp.SetHostChromeCursor(_renderer.CursorFor(part) ?? CursorForPart(part));
        Repaint(BuildState());
    }

    private void OnPointerButton(uint timeMs, uint button, bool pressed)
    {
        if (button != BtnLeft)
        {
            return;
        }

        if (!pressed)
        {
            if (_pressed != FramePart.None)
            {
                var released = _pressed;
                _pressed = FramePart.None;

                if (_pointerIn && released == _hot)
                {
                    Activate(released);
                }

                Repaint(BuildState());
            }

            return;
        }

        var part = _renderer.PartAt(_pointerX, _pointerY, BuildState(), _frame.Scale);
        switch (part)
        {
            case FramePart.Title or FramePart.Icon:
                if (timeMs - _lastTitlePressMs <= DoubleClickMillis)
                {
                    _lastTitlePressMs = 0;
                    ToggleMaximize();
                    return;
                }

                _lastTitlePressMs = timeMs;

                _frame.StartMove();
                return;
            case FramePart.TopLeft or FramePart.Top or FramePart.TopRight or
                 FramePart.Right or FramePart.BottomRight or FramePart.Bottom or
                 FramePart.BottomLeft or FramePart.Left:
                _frame.StartResize(EdgesFor(part));
                return;
            case FramePart.None or FramePart.Border:
                return;
            default:
                _pressed = part;
                Repaint(BuildState());
                return;
        }
    }

    private void Activate(FramePart part)
    {
        switch (part)
        {
            case FramePart.Close:
                _onClose();
                break;
            case FramePart.Maximize:
                ToggleMaximize();
                break;
            case FramePart.Minimize:
                _frame.SetMinimized();
                break;
        }
    }

    private void ToggleMaximize() => _frame.SetMaximized(!_frame.Maximized);

    private static HostFrameEdges EdgesFor(FramePart part) => part switch
    {
        FramePart.Top => HostFrameEdges.Top,
        FramePart.Bottom => HostFrameEdges.Bottom,
        FramePart.Left => HostFrameEdges.Left,
        FramePart.Right => HostFrameEdges.Right,
        FramePart.TopLeft => HostFrameEdges.TopLeft,
        FramePart.TopRight => HostFrameEdges.TopRight,
        FramePart.BottomLeft => HostFrameEdges.BottomLeft,
        FramePart.BottomRight => HostFrameEdges.BottomRight,
        _ => HostFrameEdges.None,
    };

    private static string CursorForPart(FramePart part) => part switch
    {
        FramePart.Top => "top_side",
        FramePart.Bottom => "bottom_side",
        FramePart.Left => "left_side",
        FramePart.Right => "right_side",
        FramePart.TopLeft => "top_left_corner",
        FramePart.TopRight => "top_right_corner",
        FramePart.BottomLeft => "bottom_left_corner",
        FramePart.BottomRight => "bottom_right_corner",
        _ => "left_ptr",
    };
}
