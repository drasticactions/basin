namespace Basin.Desktop;

/// <summary>
/// Activates the pointer constraint of the focused surface when the pointer enters its region, and applies it to pointer motion. A <see cref="PointerConstraintsManager"/> creates one when it is given a seat.
/// </summary>
public sealed class PointerConstraintDriver : IDisposable
{
    private readonly PointerConstraintsManager _constraints;
    private readonly Seat.Seat _seat;
    private readonly Action _onActiveDeactivated;
    private PointerConstraint? _active;
    private double _originX;
    private double _originY;
    private bool _disposed;

    internal PointerConstraintDriver(PointerConstraintsManager constraints, Seat.Seat seat)
    {
        _constraints = constraints;
        _seat = seat;
        _onActiveDeactivated = OnActiveDeactivated;
        _seat.Pointer.FocusChanged += OnFocusChanged;
        _seat.Pointer.Moved += OnMoved;
    }

    /// <summary>
    /// Gets or sets the policy that can refuse a constraint. A constraint for which it returns <see langword="false"/> never activates. When it is <see langword="null"/>, all constraints are allowed.
    /// </summary>
    public Func<PointerConstraint, bool>? Allow { get; set; }

    /// <summary>
    /// Gets the constraint that applies now. It is <see langword="null"/> when no constraint is active.
    /// </summary>
    public PointerConstraint? Active => _active;

    /// <summary>
    /// Gets a value indicating whether the active constraint is a lock.
    /// </summary>
    public bool IsLocked => _active is { Kind: ConstraintKind.Lock };

    /// <summary>
    /// Occurs when the active constraint changes. The arguments are the previous and the new constraint, and either can be <see langword="null"/>.
    /// </summary>
    public event Action<PointerConstraint?, PointerConstraint?>? Changed;

    /// <summary>
    /// Recomputes the active constraint from the pointer focus and position. The driver calls it on focus changes and motion, and a consumer calls it after it changes <see cref="Allow"/>.
    /// </summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var pointer = _seat.Pointer;
        PointerConstraint? next = null;
        if (pointer.Focus is { } focus && _constraints.ConstraintFor(focus) is { } candidate &&
            Allow?.Invoke(candidate) != false &&
            (candidate == _active || candidate.Contains(pointer.X, pointer.Y)))
        {
            next = candidate;
        }

        if (next == _active)
        {
            if (next is not null)
            {
                RememberOrigin();
            }

            return;
        }

        var previous = _active;
        if (previous is not null)
        {
            previous.Deactivated -= _onActiveDeactivated;
            _active = null;
            previous.Deactivate();
        }

        if (next is not null)
        {
            RememberOrigin();
            _active = next;
            next.Deactivated += _onActiveDeactivated;
            next.Activate();
        }

        Changed?.Invoke(previous, _active);
    }

    /// <summary>
    /// Applies the active constraint to a pointer move. A lock keeps the old position, and a confinement clamps the new position into the region.
    /// </summary>
    /// <param name="fromX">The X position before the move, in layout coordinates.</param>
    /// <param name="fromY">The Y position before the move, in layout coordinates.</param>
    /// <param name="layoutX">The X position after the move, changed in place.</param>
    /// <param name="layoutY">The Y position after the move, changed in place.</param>
    /// <returns><see langword="true"/> when the constraint changed the target position.</returns>
    public bool Constrain(double fromX, double fromY, ref double layoutX, ref double layoutY)
    {
        switch (_active)
        {
            case { Kind: ConstraintKind.Lock }:
                var held = layoutX != fromX || layoutY != fromY;
                (layoutX, layoutY) = (fromX, fromY);
                return held;

            case { Kind: ConstraintKind.Confine } confine:
                var surfaceX = layoutX - _originX;
                var surfaceY = layoutY - _originY;
                if (!confine.Clamp(ref surfaceX, ref surfaceY))
                {
                    return false;
                }

                (layoutX, layoutY) = (surfaceX + _originX, surfaceY + _originY);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Gets the layout position that the cursor moves to when a lock ends, from its cursor position hint.
    /// </summary>
    /// <param name="constraint">The constraint that ends.</param>
    /// <param name="layoutX">The X position in layout coordinates, or zero.</param>
    /// <param name="layoutY">The Y position in layout coordinates, or zero.</param>
    /// <returns><see langword="true"/> when <paramref name="constraint"/> is a lock with a hint.</returns>
    public bool ReleasePoint(PointerConstraint constraint, out double layoutX, out double layoutY)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        if (constraint.Kind == ConstraintKind.Lock && constraint.CursorPositionHint is { } hint)
        {
            layoutX = _originX + hint.X;
            layoutY = _originY + hint.Y;
            return true;
        }

        layoutX = 0;
        layoutY = 0;
        return false;
    }

    /// <summary>
    /// Stops listening to the pointer. It does not deactivate the active constraint.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _seat.Pointer.FocusChanged -= OnFocusChanged;
        _seat.Pointer.Moved -= OnMoved;
        if (_active is { } active)
        {
            active.Deactivated -= _onActiveDeactivated;
            _active = null;
        }
    }

    private void RememberOrigin()
    {
        var pointer = _seat.Pointer;
        _originX = pointer.LayoutX - pointer.X;
        _originY = pointer.LayoutY - pointer.Y;
    }

    private void OnFocusChanged(Surface? focus) => Refresh();

    private void OnMoved(uint timeMs, double x, double y)
    {
        if (_active is null && (_seat.Pointer.Focus is not { } focus || _constraints.ConstraintFor(focus) is null))
        {
            return;
        }

        Refresh();
    }

    private void OnActiveDeactivated()
    {
        if (_active is not { } previous)
        {
            return;
        }

        previous.Deactivated -= _onActiveDeactivated;
        _active = null;
        Changed?.Invoke(previous, null);
    }
}
