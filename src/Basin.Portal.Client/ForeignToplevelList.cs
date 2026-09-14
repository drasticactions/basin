using Basin.Capabilities;
using Basin.Portal.Client.Protocol;

namespace Basin.Portal.Client;

public sealed class ForeignToplevelList : IToplevelModel, IDisposable
{
    private readonly Dictionary<ulong, Entry> _entries = [];
    private readonly ToplevelObservers _observers = new();
    private readonly ExtForeignToplevelListV1 _list;
    private ulong _nextId;

    public ForeignToplevelList(ExtForeignToplevelListV1 list)
    {
        _list = list;
        list.Toplevel += (_, e) => Track(e.Toplevel);
    }

    public int Count => _entries.Count;

    public ExtForeignToplevelHandleV1? HandleOf(ulong id) => _entries.TryGetValue(id, out var entry) ? entry.Handle : null;

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

    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    public bool Request(ulong toplevelId, in ToplevelRequest request) => false;

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
