namespace Basin.Capabilities;

public sealed class AggregateToplevelModel : IToplevelModel
{
    private const int SourceShift = 56;

    private readonly List<IToplevelSource> _sources = [];
    private readonly ToplevelObservers _observers = new();

    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    public void Add(IToplevelSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_sources.Count >= 255)
        {
            throw new InvalidOperationException("a session cannot hold more than 255 window sources");
        }

        var index = (ulong)_sources.Count + 1;
        _sources.Add(source);
        source.AddObserver(new SourceObserver(this, index));
    }

    private sealed class SourceObserver(AggregateToplevelModel model, ulong sourceIndex) : IToplevelObserver
    {
        public void OnToplevelAdded(ulong toplevelId) =>
            model._observers.Added(Global(sourceIndex, toplevelId));

        public void OnToplevelChanged(ulong toplevelId) =>
            model._observers.Changed(Global(sourceIndex, toplevelId));

        public void OnToplevelRemoved(ulong toplevelId) =>
            model._observers.Removed(Global(sourceIndex, toplevelId));
    }

    public int Enumerate(Span<ToplevelInfo> toplevels)
    {
        var total = 0;
        for (var i = 0; i < _sources.Count; i++)
        {
            var written = _sources[i].Enumerate(toplevels[total..]);
            if (written < 0)
            {
                return -1;
            }

            var index = (ulong)i + 1;
            for (var j = 0; j < written; j++)
            {
                var info = toplevels[total + j];
                toplevels[total + j] = info with
                {
                    Id = Global(index, info.Id),
                    ParentId = info.ParentId == 0 ? 0 : Global(index, info.ParentId),
                };
            }

            total += written;
        }

        return total;
    }

    public bool TryGet(ulong toplevelId, out ToplevelInfo info)
    {
        info = default;
        if (!TrySplit(toplevelId, out var source, out var localId) || !source.TryGet(localId, out info))
        {
            return false;
        }

        var index = toplevelId >> SourceShift;
        info = info with
        {
            Id = toplevelId,
            ParentId = info.ParentId == 0 ? 0 : Global(index, info.ParentId),
        };
        return true;
    }

    public bool Request(ulong toplevelId, in ToplevelRequest request) =>
        TrySplit(toplevelId, out var source, out var localId) && source.Request(localId, request);

    private static ulong Global(ulong sourceIndex, ulong localId) => (sourceIndex << SourceShift) | localId;

    private bool TrySplit(ulong toplevelId, out IToplevelSource source, out ulong localId)
    {
        var index = (int)(toplevelId >> SourceShift) - 1;
        localId = toplevelId & ((1UL << SourceShift) - 1);
        if (index < 0 || index >= _sources.Count)
        {
            source = null!;
            return false;
        }

        source = _sources[index];
        return true;
    }
}
