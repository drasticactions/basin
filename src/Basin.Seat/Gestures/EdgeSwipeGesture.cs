namespace Basin.Seat;

/// <summary>
/// Runs an <see cref="EdgeSwipeRecognizer"/> as a member of the touch gesture chain, in layout coordinates.
/// </summary>
public sealed class EdgeSwipeGesture : ITouchGestures
{
    private EdgeSwipeArea _area;
    private bool _active;

    /// <summary>
    /// Gets the recognizer. Set its thresholds before the first touch.
    /// </summary>
    public EdgeSwipeRecognizer Recognizer { get; } = new();

    /// <summary>
    /// Gets or sets the handler that gives the area and receives the swipes. With no handler, every touch passes.
    /// </summary>
    public IEdgeSwipeHandler? Handler { get; set; }

    /// <summary>
    /// Gets a value indicating whether the gesture tracks a candidate or a claimed swipe.
    /// </summary>
    public bool IsActive => _active;

    /// <inheritdoc/>
    public TouchGestureVerdict Down(int id, uint timeMs, double x, double y)
    {
        if (_active || Handler is not { } handler || !handler.TryArea(x, y, out var area))
        {
            return TouchGestureVerdict.Pass;
        }

        if (Recognizer.Begin(id, x - area.X, y - area.Y, area.Width, area.Height, timeMs) ==
            EdgeSwipeAction.Withhold)
        {
            _area = area;
            _active = true;
            return TouchGestureVerdict.Withhold;
        }

        return TouchGestureVerdict.Pass;
    }

    /// <inheritdoc/>
    public TouchGestureVerdict Motion(int id, uint timeMs, double x, double y)
    {
        if (!_active || id != Recognizer.ContactId)
        {
            return TouchGestureVerdict.Pass;
        }

        switch (Recognizer.Update(id, x - _area.X, y - _area.Y, timeMs))
        {
            case EdgeSwipeAction.Withhold:
                return TouchGestureVerdict.Withhold;

            case EdgeSwipeAction.Claim:
                Handler?.Claimed(Recognizer);
                return TouchGestureVerdict.Claim;

            case EdgeSwipeAction.Track:
                Handler?.Track(Recognizer);
                return TouchGestureVerdict.Owned;

            case EdgeSwipeAction.Decline:
                _active = false;
                return TouchGestureVerdict.Decline;

            default:
                return TouchGestureVerdict.Pass;
        }
    }

    /// <inheritdoc/>
    public TouchGestureVerdict Up(int id, uint timeMs)
    {
        if (!_active || id != Recognizer.ContactId)
        {
            return TouchGestureVerdict.Pass;
        }

        switch (Recognizer.End(id, timeMs))
        {
            case EdgeSwipeAction.Finish:
                _active = false;
                Handler?.Finished(Recognizer);
                return TouchGestureVerdict.Finish;

            case EdgeSwipeAction.Decline:
                _active = false;
                return TouchGestureVerdict.Decline;

            default:
                return TouchGestureVerdict.Pass;
        }
    }

    /// <inheritdoc/>
    public void Cancel()
    {
        if (_active)
        {
            Recognizer.Abort();
            _active = false;
        }
    }

    /// <inheritdoc/>
    public int TakeWithheld(Span<EdgeSwipeSample> into)
    {
        var count = Recognizer.TakeWithheld(into);
        for (var i = 0; i < count; i++)
        {
            into[i] = into[i] with { X = into[i].X + _area.X, Y = into[i].Y + _area.Y };
        }

        return count;
    }
}
