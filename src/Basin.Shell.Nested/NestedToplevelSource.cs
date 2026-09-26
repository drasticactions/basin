using Basin.Capabilities;

namespace Basin.Shell.Nested;

internal sealed class NestedToplevelSource(NestedShell shell) : IToplevelSource
{
    private readonly Dictionary<ulong, ManagedWindow> _windows = [];
    private readonly Dictionary<ManagedWindow, ulong> _ids = [];
    private readonly Dictionary<ulong, ToplevelInfo> _published = [];
    private readonly ToplevelObservers _observers = new();
    private readonly ToplevelCommitObservers _commitObservers = new();
    private readonly Dictionary<ulong, Action> _commitHandlers = [];
    private ulong _nextId;

    public bool ReportsCommits => true;

    public void AddCommitObserver(IToplevelCommitObserver observer)
    {
        _commitObservers.Add(observer);
        if (_commitObservers.Count == 1)
        {
            foreach (var (id, window) in _windows)
            {
                WatchCommits(id, window);
            }
        }
    }

    public void RemoveCommitObserver(IToplevelCommitObserver observer)
    {
        _commitObservers.Remove(observer);
        if (_commitObservers.Count == 0)
        {
            foreach (var (id, handler) in _commitHandlers)
            {
                _windows[id].Content.Committed -= handler;
            }

            _commitHandlers.Clear();
        }
    }

    private void WatchCommits(ulong id, ManagedWindow window)
    {
        if (_commitHandlers.ContainsKey(id))
        {
            return;
        }

        Action handler = () =>
        {
            var client = window.ClientBox;
            _commitObservers.Committed(id, new Box(0, 0, client.Width, client.Height));
        };
        _commitHandlers[id] = handler;
        window.Content.Committed += handler;
    }

    public int Count => _windows.Count;

    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    public ulong IdOf(ManagedWindow window) => _ids.GetValueOrDefault(window);

    public ulong Add(ManagedWindow window)
    {
        var id = ++_nextId;
        _windows[id] = window;
        _ids[window] = id;
        _published[id] = Describe(id, window);
        if (_commitObservers.Count > 0)
        {
            WatchCommits(id, window);
        }

        _observers.Added(id);
        return id;
    }

    public void Remove(ManagedWindow window)
    {
        if (!_ids.Remove(window, out var id))
        {
            return;
        }

        if (_commitHandlers.Remove(id, out var handler))
        {
            window.Content.Committed -= handler;
        }

        _windows.Remove(id);
        _published.Remove(id);
        _observers.Removed(id);
    }

    public void Refresh()
    {
        foreach (var (id, window) in _windows)
        {
            var info = Describe(id, window);
            if (info == _published[id])
            {
                continue;
            }

            _published[id] = info;
            _observers.Changed(id);
        }
    }

    public int Enumerate(Span<ToplevelInfo> toplevels)
    {
        if (_windows.Count > toplevels.Length)
        {
            return -1;
        }

        var written = 0;
        foreach (var (id, window) in _windows)
        {
            toplevels[written++] = Describe(id, window);
        }

        return written;
    }

    public bool TryGet(ulong localId, out ToplevelInfo info)
    {
        if (_windows.TryGetValue(localId, out var window))
        {
            info = Describe(localId, window);
            return true;
        }

        info = default;
        return false;
    }

    public bool Request(ulong localId, in ToplevelRequest request) =>
        _windows.TryGetValue(localId, out var window) && shell.AnswerRequest(window, request);

    private ToplevelInfo Describe(ulong id, ManagedWindow window)
    {
        var state = ToplevelState.None;
        if (window.Maximized)
        {
            state |= ToplevelState.Maximized;
        }

        if (window.Minimized)
        {
            state |= ToplevelState.Minimized;
        }

        if (window.Fullscreen)
        {
            state |= ToplevelState.Fullscreen;
        }

        if (ReferenceEquals(shell.Focused, window))
        {
            state |= ToplevelState.Activated;
        }

        var parent = window.Content.Parent is { } content && shell.OwnerOf(content) is { } owner ? IdOf(owner) : 0;
        return new ToplevelInfo(
            id,
            window.Content.Title,
            window.Content.AppId,
            state,
            window.Content.Surface,
            window.IsMapped ? window.FrameBox : default,
            window.IsMapped ? window.ClientBox : default,
            ParentId: parent);
    }
}
