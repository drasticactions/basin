using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Scene;

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

    public SceneToplevelStack(Scene scene, ToplevelSceneIndex index)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(index);
        _scene = scene;
        _index = index;
    }

    public void AddObserver(IToplevelStackObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IToplevelStackObserver observer) => _observers.Remove(observer);

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
