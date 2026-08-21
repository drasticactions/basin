namespace EightWm;

internal sealed class CloseQueue
{
    private readonly List<(IClosable App, long Deadline)> _pending = [];

    public long GraceMillis { get; set; } = 10_000;

    public int Count => _pending.Count;

    public bool Holds(IClosable app)
    {
        foreach (var entry in _pending)
        {
            if (ReferenceEquals(entry.App, app))
            {
                return true;
            }
        }

        return false;
    }

    public bool Request(IClosable app, long nowMillis)
    {
        if (Holds(app))
        {
            return false;
        }

        _pending.Add((app, nowMillis + GraceMillis));
        app.RequestClose();
        return true;
    }

    public void Forget(IClosable app)
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_pending[i].App, app))
            {
                _pending.RemoveAt(i);
            }
        }
    }

    public int Expire(long nowMillis, List<IClosable> kill)
    {
        kill.Clear();
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (nowMillis < _pending[i].Deadline)
            {
                continue;
            }

            var app = _pending[i].App;
            _pending.RemoveAt(i);
            if (app.IsAttributable && app.Pid > 0)
            {
                kill.Add(app);
            }
        }

        return kill.Count;
    }
}
