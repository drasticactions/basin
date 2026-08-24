using Basin.Seat;
using Basin.Shell.Xdg;

namespace Basin.Plasma;

public sealed class PlasmaScreenEdges : IDisposable, IEdgeSwipeHandler
{
    public const int DwellMillis = 150;

    private const double EdgeSlack = 1.0;

    private readonly ICompositorEventLoop _loop;
    private readonly OutputLayout _layout;
    private readonly List<Armed> _armed = [];
    private Basin.Seat.Seat? _seat;
    private IEventSource? _timer;
    private Armed? _dwelling;
    private IOutput? _touchOutput;
    private bool _disposed;

    public PlasmaScreenEdges(ICompositorEventLoop loop, Basin.Seat.Seat? seat, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(layout);
        _loop = loop;
        _layout = layout;
        TouchGesture = new EdgeSwipeGesture { Handler = this };
        Seat = seat;
    }

    public EdgeSwipeGesture TouchGesture { get; }

    public Basin.Seat.Seat? Seat
    {
        get => _seat;
        set
        {
            if (ReferenceEquals(_seat, value) || _disposed)
            {
                return;
            }

            if (_seat is { } old)
            {
                old.Pointer.Moved -= OnMoved;
                old.Pointer.Buttoned -= OnButtoned;
            }

            _seat = value;
            if (value is { } live)
            {
                live.Pointer.Moved += OnMoved;
                live.Pointer.Buttoned += OnButtoned;
            }
        }
    }

    private sealed class Armed : IDisposable
    {
        public required PlasmaScreenEdges Owner;
        public required LayerAnchor Border;
        public required IOutput Output;
        public required Action Triggered;

        public void Dispose() => Owner.Disarm(this);
    }

    public IDisposable Arm(LayerAnchor border, IOutput output, Action triggered)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(triggered);
        var armed = new Armed { Owner = this, Border = border, Output = output, Triggered = triggered };
        _armed.Add(armed);
        return armed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_seat is { } seat)
        {
            seat.Pointer.Moved -= OnMoved;
            seat.Pointer.Buttoned -= OnButtoned;
        }

        TouchGesture.Cancel();
        _timer?.Remove();
        _timer = null;
        _armed.Clear();
    }

    bool IEdgeSwipeHandler.TryArea(double layoutX, double layoutY, out EdgeSwipeArea area)
    {
        area = default;
        if (_disposed)
        {
            return false;
        }

        foreach (var armed in _armed)
        {
            var box = _layout.BoxOf(armed.Output);
            if (box.IsEmpty ||
                layoutX < box.X || layoutX > box.X + box.Width ||
                layoutY < box.Y || layoutY > box.Y + box.Height)
            {
                continue;
            }

            var edges = ScreenEdges.None;
            foreach (var candidate in _armed)
            {
                if (candidate.Output == armed.Output)
                {
                    edges |= EdgeFlag(candidate.Border);
                }
            }

            _touchOutput = armed.Output;
            TouchGesture.Recognizer.Edges = edges;
            area = new EdgeSwipeArea(box.X, box.Y, box.Width, box.Height);
            return true;
        }

        return false;
    }

    void IEdgeSwipeHandler.Claimed(EdgeSwipeRecognizer recognizer)
    {
        var border = recognizer.Edge switch
        {
            ScreenEdge.Top => LayerAnchor.Top,
            ScreenEdge.Bottom => LayerAnchor.Bottom,
            ScreenEdge.Left => LayerAnchor.Left,
            ScreenEdge.Right => LayerAnchor.Right,
            _ => LayerAnchor.None,
        };
        foreach (var armed in _armed)
        {
            if (armed.Border == border && armed.Output == _touchOutput)
            {
                Trigger(armed);
                return;
            }
        }
    }

    void IEdgeSwipeHandler.Track(EdgeSwipeRecognizer recognizer)
    {
    }

    void IEdgeSwipeHandler.Finished(EdgeSwipeRecognizer recognizer)
    {
    }

    private static ScreenEdges EdgeFlag(LayerAnchor border) => border switch
    {
        LayerAnchor.Top => ScreenEdges.Top,
        LayerAnchor.Bottom => ScreenEdges.Bottom,
        LayerAnchor.Left => ScreenEdges.Left,
        LayerAnchor.Right => ScreenEdges.Right,
        _ => ScreenEdges.None,
    };

    private void Disarm(Armed armed)
    {
        _armed.Remove(armed);
        if (ReferenceEquals(_dwelling, armed))
        {
            _dwelling = null;
            _timer?.UpdateTimer(0);
        }
    }

    private Armed? AtPointer()
    {
        if (_seat is not { } seat)
        {
            return null;
        }

        var x = seat.Pointer.LayoutX;
        var y = seat.Pointer.LayoutY;
        foreach (var armed in _armed)
        {
            var box = _layout.BoxOf(armed.Output);
            if (box.IsEmpty ||
                x < box.X - EdgeSlack || x > box.X + box.Width + EdgeSlack ||
                y < box.Y - EdgeSlack || y > box.Y + box.Height + EdgeSlack)
            {
                continue;
            }

            var atEdge = armed.Border switch
            {
                LayerAnchor.Top => y <= box.Y + EdgeSlack,
                LayerAnchor.Bottom => y >= box.Y + box.Height - EdgeSlack,
                LayerAnchor.Left => x <= box.X + EdgeSlack,
                LayerAnchor.Right => x >= box.X + box.Width - EdgeSlack,
                _ => false,
            };
            if (atEdge)
            {
                return armed;
            }
        }

        return null;
    }

    private void OnMoved(uint timeMs, double dx, double dy)
    {
        var hit = AtPointer();
        if (ReferenceEquals(hit, _dwelling))
        {
            return;
        }

        _dwelling = hit;
        if (hit is null)
        {
            _timer?.UpdateTimer(0);
            return;
        }

        _timer ??= _loop.AddTimer(OnDwellElapsed);
        _timer.UpdateTimer(DwellMillis);
    }

    private void OnButtoned(uint button, bool pressed)
    {
        if (pressed && AtPointer() is { } hit)
        {
            Trigger(hit);
        }
    }

    private void OnDwellElapsed()
    {
        if (_dwelling is { } armed && ReferenceEquals(AtPointer(), armed))
        {
            Trigger(armed);
        }

        _dwelling = null;
    }

    private void Trigger(Armed armed)
    {
        Disarm(armed);
        armed.Triggered();
    }
}
