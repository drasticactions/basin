namespace Basin.Scene;

/// <summary>
/// Keeps a chain of named <see cref="SceneTransform"/> nodes between a window root and its content, ordered by z-order. A higher z-order wraps the lower ones.
/// </summary>
public sealed class TransformStack
{
    /// <summary>
    /// The standard z-order values for <see cref="Add"/>.
    /// </summary>
    public static class ZOrder
    {
        /// <summary>
        /// The z-order for a 2D placement transform. It is the lowest standard value.
        /// </summary>
        public const int Transform2D = 1;

        /// <summary>
        /// The z-order for a 3D projection.
        /// </summary>
        public const int Transform3D = 2;

        /// <summary>
        /// The z-order for an effect such as an open or close animation.
        /// </summary>
        public const int Effect = 500;

        /// <summary>
        /// The z-order for a backdrop effect. It is the highest standard value.
        /// </summary>
        public const int Backdrop = 1000;
    }

    private readonly SceneTree _root;
    private readonly List<(int Z, string Name, SceneTransform Node)> _entries = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TransformStack"/> class with no transforms.
    /// </summary>
    /// <param name="windowRoot">The window tree whose children the transforms wrap.</param>
    public TransformStack(SceneTree windowRoot)
    {
        _root = windowRoot;
    }

    /// <summary>
    /// Gets the window tree that the stack was created for.
    /// </summary>
    public SceneTree Root => _root;

    /// <summary>
    /// Inserts a transform at its z-order. It moves the children of the next outer transform, or of <see cref="Root"/>, into the new transform. Equal z-orders go inside the existing one.
    /// </summary>
    /// <param name="zOrder">The z-order. A higher value wraps a lower one.</param>
    /// <param name="name">The unique name of the transform.</param>
    /// <returns>The new transform, with <see cref="SceneNode.DebugName"/> set to <paramref name="name"/>.</returns>
    /// <exception cref="InvalidOperationException">A live transform in the stack already has <paramref name="name"/>.</exception>
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
        var node = new SceneTransform(parent) { DebugName = name };
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

    /// <summary>
    /// Finds a live transform by name. Destroyed transforms are dropped from the stack first.
    /// </summary>
    /// <param name="name">The name given to <see cref="Add"/>.</param>
    /// <returns>The transform, or <see langword="null"/> when no live transform has the name.</returns>
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

    /// <summary>
    /// Removes a transform, moves its children back to its parent in the same order and destroys it.
    /// </summary>
    /// <param name="name">The name given to <see cref="Add"/>.</param>
    /// <returns><see langword="false"/> when no live transform has the name.</returns>
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
