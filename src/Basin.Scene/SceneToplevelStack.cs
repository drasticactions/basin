using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Scene;

/// <summary>
/// Implements <see cref="IToplevelStack"/> from the scene: the stacking order is the depth-first order of the content trees in a <see cref="ToplevelSceneIndex"/>.
/// </summary>
public sealed class SceneToplevelStack : IToplevelStack
{
    private readonly ThreadAffinity _affinity = ThreadAffinity.Capture();
    private readonly Scene _scene;
    private readonly ToplevelSceneIndex _index;
    private readonly ToplevelStackObservers _observers = new();
    private ulong[] _order = [];
    private int _count;
    private ulong[] _previous = [];
    private int _previousCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneToplevelStack"/> class.
    /// </summary>
    /// <param name="scene">The scene to walk.</param>
    /// <param name="index">The index that maps scene trees to toplevel ids.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="index"/> is <see langword="null"/>.</exception>
    public SceneToplevelStack(Scene scene, ToplevelSceneIndex index)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(index);
        _scene = scene;
        _index = index;
    }

    /// <inheritdoc/>
    public void AddObserver(IToplevelStackObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IToplevelStackObserver observer) => _observers.Remove(observer);

    /// <inheritdoc/>
    public int Enumerate(Span<ulong> toplevels)
    {
        _affinity.Assert();
        Recompute();
        if (_count > toplevels.Length)
        {
            return -1;
        }

        _order.AsSpan(0, _count).CopyTo(toplevels);
        return _count;
    }

    /// <summary>
    /// Finds the toplevel that owns the topmost input node at a scene point. The scene answers every query, so the method always returns <see langword="true"/>.
    /// </summary>
    /// <param name="x">The horizontal scene coordinate, in logical pixels.</param>
    /// <param name="y">The vertical scene coordinate, in logical pixels.</param>
    /// <param name="toplevelId">The toplevel id, or 0 when no toplevel is at the point.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool TryToplevelAt(double x, double y, out ulong toplevelId)
    {
        _affinity.Assert();
        toplevelId = 0;
        if (_scene.NodeAt(x, y) is { } hit)
        {
            _ = _index.TryOwnerOf(hit.Node, out toplevelId);
        }

        return true;
    }

    /// <summary>
    /// Recomputes the stacking order and notifies the observers if the order differs from the last notified order. Call it after a restack.
    /// </summary>
    public void RaiseChanged()
    {
        _affinity.Assert();
        Recompute();
        if (_count == _previousCount && _order.AsSpan(0, _count).SequenceEqual(_previous.AsSpan(0, _previousCount)))
        {
            return;
        }

        if (_previous.Length < _count)
        {
            _previous = new ulong[_order.Length];
        }

        _order.AsSpan(0, _count).CopyTo(_previous);
        _previousCount = _count;
        _observers.Changed();
    }

    private void Recompute()
    {
        _index.ResolveStale();
        _count = 0;
        Walk(_scene.Root);
    }

    private void Walk(SceneNode node)
    {
        if (_index.TryIdOf(node, out var id))
        {
            if (_count == _order.Length)
            {
                Array.Resize(ref _order, Math.Max(8, _order.Length * 2));
            }

            _order[_count++] = id;
        }

        if (node is SceneTree tree)
        {
            foreach (var child in tree.Children)
            {
                Walk(child);
            }
        }
    }
}
