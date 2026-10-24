using Basin.Capabilities;
using Basin.Portal.Client.Protocol;

namespace Basin.Portal.Client;

/// <summary>
/// Implements <see cref="IToplevelModel"/> over <c>ext_foreign_toplevel_list_v1</c> for a portal client.
/// It reports only the title and the app id, and it cannot act on a toplevel.
/// </summary>
public sealed class ForeignToplevelList : IToplevelModel, IDisposable
{
    private readonly Dictionary<ulong, Entry> _entries = [];
    private readonly ToplevelObservers _observers = new();
    private readonly ExtForeignToplevelListV1 _list;
    private ulong _nextId;

    /// <summary>
    /// Initializes a new instance of the <see cref="ForeignToplevelList"/> class. The instance takes ownership of the list proxy.
    /// </summary>
    /// <param name="list">The bound foreign toplevel list.</param>
    public ForeignToplevelList(ExtForeignToplevelListV1 list)
    {
        _list = list;
        list.Toplevel += (_, e) => Track(e.Toplevel);
    }

    /// <summary>
    /// Gets the number of toplevels that received their first <c>done</c> event and are not closed.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Gets the wire handle of a toplevel, for example to make a capture source for it.
    /// </summary>
    /// <param name="id">The toplevel id that this model assigned.</param>
    /// <returns>The handle, or <see langword="null"/> when the id is unknown.</returns>
    public ExtForeignToplevelHandleV1? HandleOf(ulong id) => _entries.TryGetValue(id, out var entry) ? entry.Handle : null;

    /// <summary>
    /// Writes a snapshot of every known toplevel into a span.
    /// </summary>
    /// <param name="toplevels">The span that receives the snapshots.</param>
    /// <returns>The number of toplevels written, or -1 when the span is too short.</returns>
    public int Enumerate(Span<ToplevelInfo> toplevels)
    {
        if (toplevels.Length < _entries.Count)
        {
            return -1;
        }

        var i = 0;
        foreach (var entry in _entries.Values)
        {
            toplevels[i++] = entry.Info;
        }

        return i;
    }

    /// <inheritdoc/>
    public bool TryGet(ulong toplevelId, out ToplevelInfo info)
    {
        if (_entries.TryGetValue(toplevelId, out var entry))
        {
            info = entry.Info;
            return true;
        }

        info = default;
        return false;
    }

    /// <inheritdoc/>
    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Refuses every request, because the foreign toplevel list protocol cannot act on a window.
    /// </summary>
    /// <param name="toplevelId">The toplevel id. It is ignored.</param>
    /// <param name="request">The request. It is ignored.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool Request(ulong toplevelId, in ToplevelRequest request) => false;

    /// <summary>
    /// Destroys every handle, stops the list and destroys it. It does not notify the observers.
    /// </summary>
    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            if (!entry.Handle.IsDestroyed)
            {
                entry.Handle.Destroy();
            }
        }

        _entries.Clear();
        if (!_list.IsDestroyed)
        {
            _list.Stop();
            _list.Destroy();
        }
    }

    private void Track(ExtForeignToplevelHandleV1 handle)
    {
        var id = ++_nextId;
        var entry = new Entry(id, handle);
        handle.Title += (_, e) => entry.Title = e.Title;
        handle.AppId += (_, e) => entry.AppId = e.AppId;
        handle.Done += (_, _) =>
        {
            var added = !_entries.ContainsKey(id);
            _entries[id] = entry;
            if (added)
            {
                _observers.Added(id);
            }
            else
            {
                _observers.Changed(id);
            }
        };
        handle.Closed += (_, _) =>
        {
            if (_entries.Remove(id))
            {
                _observers.Removed(id);
            }

            if (!handle.IsDestroyed)
            {
                handle.Destroy();
            }
        };
    }

    private sealed class Entry(ulong id, ExtForeignToplevelHandleV1 handle)
    {
        public ExtForeignToplevelHandleV1 Handle { get; } = handle;

        public string Title { get; set; } = "";

        public string AppId { get; set; } = "";

        public ToplevelInfo Info => new(id, Title, AppId, ToplevelState.None, null, default);
    }
}
