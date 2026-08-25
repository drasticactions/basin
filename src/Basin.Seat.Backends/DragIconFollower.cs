using Basin.Scene;

namespace Basin.Seat.Backends;

public sealed class DragIconFollower
{
    private readonly Basin.Seat.Seat _seat;
    private readonly Func<SceneTree> _layer;
    private readonly Func<(double X, double Y)> _pointer;
    private SceneSurface? _icon;
    private int _touchSlot = -1;

    public DragIconFollower(Basin.Seat.Seat seat, Func<SceneTree> layer, Func<(double X, double Y)> pointer)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(pointer);
        _seat = seat;
        _layer = layer;
        _pointer = pointer;
        _seat.DataDevice.DragStarted += OnDragStarted;
        _seat.DataDevice.DragEnded += OnDragEnded;
    }

    public TouchRouter? Touch { get; set; }

    public event Action<SceneSurface>? Created;

    public SceneSurface? Icon => _icon is { IsDestroyed: false } icon ? icon : null;

    public void SetTouchSlot(int id) => _touchSlot = id;

    public void Follow()
    {
        if (_touchSlot >= 0 && Touch is { } router && !router.TryGetPosition(_touchSlot, out _, out _))
        {
            _touchSlot = -1;
        }

        if (_icon is not { IsDestroyed: false } icon)
        {
            return;
        }

        if (_touchSlot >= 0 && Touch is { } touch && touch.TryGetPosition(_touchSlot, out var x, out var y))
        {
            icon.Tree.SetPosition((int)x, (int)y);
            return;
        }

        var (pointerX, pointerY) = _pointer();
        icon.Tree.SetPosition((int)pointerX, (int)pointerY);
    }

    private void OnDragStarted(DragEvent drag)
    {
        if (drag.Icon is not { } surface)
        {
            return;
        }

        _icon = new SceneSurface(_layer(), surface) { InputEnabled = false };
        Created?.Invoke(_icon);
        Follow();
    }

    private void OnDragEnded()
    {
        if (_icon is { IsDestroyed: false } icon)
        {
            icon.Destroy();
        }

        _icon = null;
        _touchSlot = -1;
    }
}
