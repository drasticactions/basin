namespace Basin.Scene;

public sealed class TransformStack
{
    public static class ZOrder
    {
        public const int Transform2D = 1;

        public const int Transform3D = 2;

        public const int Effect = 500;

        public const int Backdrop = 1000;
    }

    private readonly SceneTree _root;
    private readonly List<(int Z, string Name, SceneTransform Node)> _entries = [];

    public TransformStack(SceneTree windowRoot)
    {
        _root = windowRoot;
    }

    public SceneTree Root => _root;

    public SceneTransform Add(int zOrder, string name)
    {
        Prune();
        if (Get(name) is not null)
        {
            throw new InvalidOperationException($"a transformer named '{name}' is already in the stack");
        }

        var index = 0;
        while (index < _entries.Count && _entries[index].Z < zOrder)
        {
            index++;
        }

        var parent = index == _entries.Count ? _root : _entries[index].Node;
        var node = new SceneTransform(parent);
        for (var i = parent.Children.Count - 1; i >= 0; i--)
        {
            var child = parent.Children[i];
            if (!ReferenceEquals(child, node))
            {
                child.Reparent(node);
            }
        }

        node.Children.Reverse();
        _entries.Insert(index, (zOrder, name, node));
        return node;
    }

    public SceneTransform? Get(string name)
    {
        Prune();
        foreach (var (_, entryName, node) in _entries)
        {
            if (entryName == name)
            {
                return node;
            }
        }

        return null;
    }

    public bool Remove(string name)
    {
        Prune();
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Name != name)
            {
                continue;
            }

            var node = _entries[i].Node;
            _entries.RemoveAt(i);
            if (node.Parent is { } parent)
            {
                var moved = node.Children.Count;
                for (var c = moved - 1; c >= 0; c--)
                {
                    node.Children[c].Reparent(parent);
                }

                parent.Children.Reverse(parent.Children.Count - moved, moved);
            }

            node.Destroy();
            return true;
        }

        return false;
    }

    private void Prune()
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Node.IsDestroyed)
            {
                _entries.RemoveAt(i);
            }
        }
    }
}
