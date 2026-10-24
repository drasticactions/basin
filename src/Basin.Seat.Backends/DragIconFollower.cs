using Basin.Scene;

namespace Basin.Seat.Backends;

/// <summary>
/// Puts the drag icon of a drag-and-drop in a scene layer and moves it with the pointer or with one touch contact. It removes the icon when the drag ends.
/// </summary>
public sealed class DragIconFollower
{
    private readonly Basin.Seat.Seat _seat;
    private readonly Func<SceneTree> _layer;
    private readonly Func<(double X, double Y)> _pointer;
    private SceneSurface? _icon;
    private int _touchSlot = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="DragIconFollower"/> class. It subscribes to the seat's drag start and end.
    /// </summary>
    /// <param name="seat">The seat whose data device reports the drags.</param>
    /// <param name="layer">The callback that gives the scene tree for the icon at drag start.</param>
    /// <param name="pointer">The callback that gives the pointer position in layout coordinates.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets or sets the touch router that gives the position of the touch slot from <see cref="SetTouchSlot"/>.
    /// </summary>
    public TouchRouter? Touch { get; set; }

    /// <summary>
    /// Occurs when a drag with an icon starts. The argument is the new scene node of the icon.
    /// </summary>
    public event Action<SceneSurface>? Created;

    /// <summary>
    /// Gets the icon node of the current drag, or <see langword="null"/> when no drag icon exists.
    /// </summary>
    public SceneSurface? Icon => _icon is { IsDestroyed: false } icon ? icon : null;

    /// <summary>
    /// Makes the icon follow a touch contact instead of the pointer. The slot is forgotten when the contact ends or the drag ends.
    /// </summary>
    /// <param name="id">The touch contact id.</param>
    public void SetTouchSlot(int id) => _touchSlot = id;

    /// <summary>
    /// Moves the icon to the touch contact or, without one, to the pointer. The consumer calls it after each motion.
    /// </summary>
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
