namespace Basin.Capabilities;

/// <summary>
/// Combines up to 255 <see cref="IToplevelSource"/> instances into one <see cref="IToplevelModel"/>.
/// A global id holds the source index in its top 8 bits and the local id of the source in the low 56 bits.
/// </summary>
public sealed class AggregateToplevelModel : IToplevelModel, IToplevelGeometryFeed
{
    private const int SourceShift = 56;

    private readonly List<IToplevelSource> _sources = [];
    private readonly ToplevelObservers _observers = new();
    private readonly ToplevelCommitObservers _commitObservers = new();
    private readonly List<SourceCommitObserver> _commitForwarders = [];

    /// <inheritdoc/>
    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Adds a source and forwards its notifications with global ids.
    /// The model keeps the sources in the order of addition and cannot remove a source.
    /// </summary>
    /// <param name="source">The source to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The model already holds 255 sources.</exception>
    public void Add(IToplevelSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_sources.Count >= 255)
        {
            throw new InvalidOperationException("A session can hold at most 255 window sources.");
        }

        var index = (ulong)_sources.Count + 1;
        _sources.Add(source);
        source.AddObserver(new SourceObserver(this, index));
        if (_commitObservers.Count > 0)
        {
            AttachCommits(source, index);
        }
    }

    /// <inheritdoc/>
    public bool ReportsCommits(ulong toplevelId) =>
        TrySplit(toplevelId, out var source, out var localId) && source.ReportsCommits && source.TryGet(localId, out _);

    /// <inheritdoc/>
    public bool IsConfigurePending(ulong toplevelId) =>
        TrySplit(toplevelId, out var source, out var localId) && source.IsConfigurePending(localId);

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void ReportFrom(ReadOnlySpan<SurfaceBox> boxes)
    {
        for (var i = 0; i < _sources.Count; i++)
        {
            if (_sources[i] is IToplevelGeometryFeed feed)
            {
                feed.ReportFrom(boxes);
            }
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <summary>
    /// Converts the local id of a toplevel in <paramref name="source"/> to the global id that this model reports.
    /// </summary>
    /// <param name="source">A source that <see cref="Add"/> added.</param>
    /// <param name="localId">The id that the source uses for the toplevel.</param>
    /// <returns>The global id, or 0 when the model does not hold the source or <paramref name="localId"/> is 0.</returns>
    public ulong GlobalId(IToplevelSource source, ulong localId)
    {
        ArgumentNullException.ThrowIfNull(source);
        var index = _sources.IndexOf(source);
        return index < 0 || localId == 0 ? 0 : Global((ulong)index + 1, localId);
    }

    /// <inheritdoc/>
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
