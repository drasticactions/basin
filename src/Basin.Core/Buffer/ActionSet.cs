namespace Basin;

internal struct ActionSet
{
    private Action? _first;
    private List<Action?>? _rest;
    private int _raising;
    private bool _holes;

    public void Add(Action? handler)
    {
        if (handler is null)
        {
            return;
        }

        if (_first is null)
        {
            _first = handler;
            return;
        }

        (_rest ??= []).Add(handler);
    }

    public void Remove(Action? handler)
    {
        if (handler is null)
        {
            return;
        }

        if (_first is not null && _first.Equals(handler))
        {
            _first = null;
            return;
        }

        if (_rest is not { } rest)
        {
            return;
        }

        for (var i = 0; i < rest.Count; i++)
        {
            if (rest[i] is { } candidate && candidate.Equals(handler))
            {
                if (_raising > 0)
                {
                    rest[i] = null;
                    _holes = true;
                }
                else
                {
                    rest.RemoveAt(i);
                }

                return;
            }
        }
    }

    public void Raise()
    {
        _raising++;
        try
        {
            _first?.Invoke();
            if (_rest is { } rest)
            {
                var count = rest.Count;
                for (var i = 0; i < count; i++)
                {
                    rest[i]?.Invoke();
                }
            }
        }
        finally
        {
            _raising--;
            if (_raising == 0 && _holes)
            {
                Compact();
            }
        }
    }

    private void Compact()
    {
        _holes = false;
        if (_rest is not { } rest)
        {
            return;
        }

        var write = 0;
        for (var read = 0; read < rest.Count; read++)
        {
            if (rest[read] is { } handler)
            {
                rest[write] = handler;
                write++;
            }
        }

        rest.RemoveRange(write, rest.Count - write);
    }
}
