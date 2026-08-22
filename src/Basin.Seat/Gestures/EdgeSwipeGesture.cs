namespace Basin.Seat;

public sealed class EdgeSwipeGesture : ITouchGestures
{
    private EdgeSwipeArea _area;
    private bool _active;

    public EdgeSwipeRecognizer Recognizer { get; } = new();

    public IEdgeSwipeHandler? Handler { get; set; }

    public bool IsActive => _active;

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

    public void Cancel()
    {
        if (_active)
        {
            Recognizer.Abort();
            _active = false;
        }
    }

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
