using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// Fills a <see cref="ToplevelSceneIndex"/> from the notifications of a toplevel model, and tells the stack about each change.
/// A toplevel with no live content tree is resolved again on the next read of the index or the stack.
/// </summary>
public sealed class ToplevelCaptureIndexObserver : IToplevelObserver
{
    private readonly IToplevelModel _toplevels;
    private readonly ToplevelSceneIndex _index;
    private readonly SceneToplevelStack _stack;
    private readonly Func<Surface, ToplevelCaptureTrees?> _resolve;
    private readonly HashSet<ulong> _stale = [];
    private readonly Dictionary<ulong, ContentWatch> _watches = [];
    private ulong[] _scratch = [];
    private bool _resolving;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToplevelCaptureIndexObserver"/> class. The caller adds it to the model.
    /// The observer becomes the resolver that <paramref name="index"/> calls before a read.
    /// </summary>
    /// <param name="toplevels">The model to read toplevel info from.</param>
    /// <param name="index">The index to fill.</param>
    /// <param name="stack">The stack to notify after each index change.</param>
    /// <param name="resolve">A function that finds the scene nodes of a surface, or returns <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ToplevelCaptureIndexObserver(
        IToplevelModel toplevels,
        ToplevelSceneIndex index,
        SceneToplevelStack stack,
        Func<Surface, ToplevelCaptureTrees?> resolve)
    {
        ArgumentNullException.ThrowIfNull(toplevels);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(resolve);
        _toplevels = toplevels;
        _index = index;
        _stack = stack;
        _resolve = resolve;
        index.Resolver = this;
    }

    /// <summary>
    /// Resolves the scene nodes of the new toplevel and adds them to the index when the content node exists.
    /// </summary>
    /// <param name="toplevelId">The id of the new toplevel.</param>
    public void OnToplevelAdded(ulong toplevelId) => Refresh(toplevelId);

    /// <summary>
    /// Resolves the scene nodes of the toplevel again, and replaces or removes the entry when the result differs.
    /// </summary>
    /// <param name="toplevelId">The id of the changed toplevel.</param>
    public void OnToplevelChanged(ulong toplevelId) => Refresh(toplevelId);

    /// <summary>
    /// Removes the toplevel from the index and notifies the stack.
    /// </summary>
    /// <param name="toplevelId">The id of the removed toplevel.</param>
    public void OnToplevelRemoved(ulong toplevelId)
    {
        _stale.Remove(toplevelId);
        Unwatch(toplevelId);
        _index.Remove(toplevelId);
        _stack.RaiseChanged();
    }

    /// <summary>
    /// Resolves the scene nodes of a toplevel again, and notifies the stack when the index changed.
    /// Call it after the compositor rebuilds the trees of a toplevel outside a model notification.
    /// </summary>
    /// <param name="toplevelId">The id of the toplevel.</param>
    public void Refresh(ulong toplevelId)
    {
        if (Resolve(toplevelId))
        {
            _stack.RaiseChanged();
        }
    }

    internal void ResolveStale()
    {
        if (_stale.Count == 0 || _resolving)
        {
            return;
        }

        _resolving = true;
        try
        {
            if (_scratch.Length < _stale.Count)
            {
                _scratch = new ulong[Math.Max(8, _stale.Count * 2)];
            }

            var count = 0;
            foreach (var id in _stale)
            {
                _scratch[count++] = id;
            }

            var changed = false;
            for (var i = 0; i < count; i++)
            {
                changed |= Resolve(_scratch[i]);
            }

            if (changed)
            {
                _stack.RaiseChanged();
            }
        }
        finally
        {
            _resolving = false;
        }
    }

    private bool Resolve(ulong toplevelId)
    {
        if (!_toplevels.TryGet(toplevelId, out var info))
        {
            _stale.Remove(toplevelId);
            return false;
        }

        var result = info.Surface is { } surface ? _resolve(surface) : null;
        if (result is { Content: { IsDestroyed: false } content } trees)
        {
            _stale.Remove(toplevelId);
            if (_index.TryGetEntry(toplevelId, out var current) && current == trees)
            {
                return false;
            }

            _index.Set(toplevelId, trees);
            Watch(toplevelId, content);
            return true;
        }

        _stale.Add(toplevelId);
        Unwatch(toplevelId);
        if (!_index.TryGetEntry(toplevelId, out _))
        {
            return false;
        }

        _index.Remove(toplevelId);
        return true;
    }

    private void Watch(ulong toplevelId, SceneNode content)
    {
        if (_watches.TryGetValue(toplevelId, out var watch))
        {
            if (ReferenceEquals(watch.Node, content))
            {
                return;
            }

            watch.Node.Destroyed -= watch.Handler;
        }

        watch = new ContentWatch(this, toplevelId, content);
        content.Destroyed += watch.Handler;
        _watches[toplevelId] = watch;
    }

    private void Unwatch(ulong toplevelId)
    {
        if (_watches.Remove(toplevelId, out var watch))
        {
            watch.Node.Destroyed -= watch.Handler;
        }
    }

    private sealed class ContentWatch
    {
        public ContentWatch(ToplevelCaptureIndexObserver owner, ulong toplevelId, SceneNode node)
        {
            Node = node;
            Handler = () => owner._stale.Add(toplevelId);
        }

        public SceneNode Node { get; }

        public Action Handler { get; }
    }
}
