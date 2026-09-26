namespace Basin.Capabilities;

public sealed class AggregateToplevelModel : IToplevelModel
{
    private const int SourceShift = 56;

    private readonly List<IToplevelSource> _sources = [];
    private readonly ToplevelObservers _observers = new();
    private readonly ToplevelCommitObservers _commitObservers = new();
    private readonly List<SourceCommitObserver> _commitForwarders = [];

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
        if (_commitObservers.Count > 0)
        {
            AttachCommits(source, index);
        }
    }

    public bool ReportsCommits(ulong toplevelId) =>
        TrySplit(toplevelId, out var source, out var localId) && source.ReportsCommits && source.TryGet(localId, out _);

    public void AddCommitObserver(IToplevelCommitObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _commitObservers.Add(observer);
        if (_commitObservers.Count == 1 && _commitForwarders.Count == 0)
        {
            for (var i = 0; i < _sources.Count; i++)
            {
                AttachCommits(_sources[i], (ulong)i + 1);
            }
        }
    }

    public void RemoveCommitObserver(IToplevelCommitObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _commitObservers.Remove(observer);
        if (_commitObservers.Count > 0)
        {
            return;
        }

        foreach (var forwarder in _commitForwarders)
        {
            forwarder.Source.RemoveCommitObserver(forwarder);
        }

        _commitForwarders.Clear();
    }

    private void AttachCommits(IToplevelSource source, ulong index)
    {
        if (!source.ReportsCommits)
        {
            return;
        }

        var forwarder = new SourceCommitObserver(this, source, index);
        _commitForwarders.Add(forwarder);
        source.AddCommitObserver(forwarder);
    }

    private sealed class SourceCommitObserver(AggregateToplevelModel model, IToplevelSource source, ulong sourceIndex)
        : IToplevelCommitObserver
    {
        public IToplevelSource Source => source;

        public void OnToplevelCommitted(ulong toplevelId, in Box damage) =>
            model._commitObservers.Committed(Global(sourceIndex, toplevelId), damage);
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

    public ulong GlobalId(IToplevelSource source, ulong localId)
    {
        ArgumentNullException.ThrowIfNull(source);
        var index = _sources.IndexOf(source);
        return index < 0 || localId == 0 ? 0 : Global((ulong)index + 1, localId);
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
