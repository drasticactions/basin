namespace Basin.Seat;

public sealed class TouchGestureSet : ITouchGestures
{
    private readonly ITouchGestures[] _members;
    private ITouchGestures? _owner;
    private int _contacts;

    public TouchGestureSet(params ITouchGestures[] members)
    {
        ArgumentNullException.ThrowIfNull(members);
        _members = members;
    }

    public ITouchGestures? Owner => _owner;

    public TouchGestureVerdict Down(int id, uint timeMs, double x, double y)
    {
        _contacts++;
        if (_owner is { } owner)
        {
            return owner.Down(id, timeMs, x, y);
        }

        var verdict = TouchGestureVerdict.Pass;
        for (var i = 0; i < _members.Length; i++)
        {
            verdict = Combine(verdict, _members[i].Down(id, timeMs, x, y), i);
            if (_owner is not null)
            {
                return verdict;
            }
        }

        return verdict;
    }

    public TouchGestureVerdict Motion(int id, uint timeMs, double x, double y)
    {
        if (_owner is { } owner)
        {
            return owner.Motion(id, timeMs, x, y);
        }

        var verdict = TouchGestureVerdict.Pass;
        for (var i = 0; i < _members.Length; i++)
        {
            verdict = Combine(verdict, _members[i].Motion(id, timeMs, x, y), i);
            if (_owner is not null)
            {
                return verdict;
            }
        }

        return verdict;
    }

    public TouchGestureVerdict Up(int id, uint timeMs)
    {
        _contacts = Math.Max(0, _contacts - 1);
        TouchGestureVerdict verdict;
        if (_owner is { } owner)
        {
            verdict = owner.Up(id, timeMs);
        }
        else
        {
            verdict = TouchGestureVerdict.Pass;
            for (var i = 0; i < _members.Length; i++)
            {
                verdict = Combine(verdict, _members[i].Up(id, timeMs), i);
            }
        }

        if (_contacts == 0)
        {
            _owner = null;
        }

        return verdict;
    }

    public void Cancel()
    {
        for (var i = 0; i < _members.Length; i++)
        {
            _members[i].Cancel();
        }

        _owner = null;
        _contacts = 0;
    }

    public int TakeWithheld(Span<EdgeSwipeSample> into)
    {
        if (_owner is { } owner)
        {
            return owner.TakeWithheld(into);
        }

        for (var i = 0; i < _members.Length; i++)
        {
            var taken = _members[i].TakeWithheld(into);
            if (taken > 0)
            {
                return taken;
            }
        }

        return 0;
    }

    private TouchGestureVerdict Combine(TouchGestureVerdict so, TouchGestureVerdict next, int index)
    {
        if (next == TouchGestureVerdict.Claim)
        {
            _owner = _members[index];
            for (var i = 0; i < _members.Length; i++)
            {
                if (i != index)
                {
                    _members[i].Cancel();
                }
            }

            return next;
        }

        return Rank(next) > Rank(so) ? next : so;
    }

    private static int Rank(TouchGestureVerdict verdict) => verdict switch
    {
        TouchGestureVerdict.Withhold => 4,
        TouchGestureVerdict.Owned => 3,
        TouchGestureVerdict.Finish => 3,
        TouchGestureVerdict.Decline => 2,
        TouchGestureVerdict.Claim => 5,
        _ => 0,
    };
}
