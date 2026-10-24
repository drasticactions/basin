using Basin.Diagnostics;

namespace Basin.Scene;

/// <summary>
/// A frozen copy of a subtree that keeps showing the last buffers after the live subtree changes or goes away, for example for a close animation.
/// </summary>
public sealed class SceneSnapshot : IDisposable
{
    private readonly List<SceneBuffer> _nodes = [];
    private SceneTree? _source;
    private Scene? _scene;
    private bool _destroyed;

    private SceneSnapshot(SceneTree tree)
    {
        Tree = tree;
        BasinCounters.Track();
    }

    /// <summary>
    /// Copies the tree of a scene surface into a new subtree.
    /// </summary>
    /// <param name="source">The surface whose tree to copy.</param>
    /// <param name="parent">The tree to add the copy to.</param>
    /// <returns>The snapshot. The caller owns it and must destroy it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="parent"/> is <see langword="null"/>.</exception>
    public static SceneSnapshot Capture(SceneSurface source, SceneTree parent)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Capture(source.Tree, parent);
    }

    /// <summary>
    /// Copies the enabled trees, transforms, buffers and rectangles of a subtree into a new subtree. The copy locks the same buffers. While the live tree is disabled, its frame callbacks go out through the copy.
    /// </summary>
    /// <param name="source">The subtree to copy.</param>
    /// <param name="parent">The tree to add the copy to.</param>
    /// <returns>The snapshot. The caller owns it and must destroy it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="parent"/> is <see langword="null"/>.</exception>
    public static SceneSnapshot Capture(SceneTree source, SceneTree parent)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parent);

        var tree = new SceneTree(parent) { Alpha = source.Alpha };
        tree.SetPosition(source.X, source.Y);
        tree.ClipBox = source.ClipBox;

        var snapshot = new SceneSnapshot(tree);
        snapshot.CopyTree(source, tree);
        snapshot._source = source;
        snapshot._scene = parent.OwnerIfAttached();
        snapshot._scene?.Hold(source, tree);
        return snapshot;
    }

    /// <summary>
    /// Gets the root of the copy. It has the position, clip and alpha of the source.
    /// </summary>
    public SceneTree Tree { get; }

    /// <summary>
    /// Gets the number of buffer nodes in the copy. It is 0 after <see cref="Destroy"/>.
    /// </summary>
    public int NodeCount => _nodes.Count;

    /// <summary>
    /// Gets a value indicating whether <see cref="Destroy"/> has run.
    /// </summary>
    public bool IsDestroyed => _destroyed;

    /// <summary>
    /// Releases the copied buffers, ends the frame callback forwarding and destroys <see cref="Tree"/>. A second call does nothing.
    /// </summary>
    public void Destroy()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        if (_source is not null)
        {
            _scene?.Release(_source, Tree);
        }

        foreach (var node in _nodes)
        {
            node.SetBuffer(null);
        }

        _nodes.Clear();
        Tree.Destroy();
        BasinCounters.Untrack();
    }

    /// <summary>
    /// Calls <see cref="Destroy"/>.
    /// </summary>
    public void Dispose() => Destroy();

    private void CopyTree(SceneTree source, SceneTree target)
    {
        foreach (var child in source.Children)
        {
            if (!child.Enabled)
            {
                continue;
            }

            switch (child)
            {
                case SceneTransform transform:
                {
                    var copy = new SceneTransform(target)
                    {
                        Alpha = transform.Alpha,
                        Matrix = transform.Matrix,
                    };
                    copy.SetPosition(transform.X, transform.Y);
                    copy.ClipBox = transform.ClipBox;
                    CopyTree(transform, copy);
                    copy.Deformer = transform.Deformer;
                    break;
                }

                case SceneTree subtree:
                {
                    var copy = new SceneTree(target) { Alpha = subtree.Alpha };
                    copy.SetPosition(subtree.X, subtree.Y);
                    copy.ClipBox = subtree.ClipBox;
                    CopyTree(subtree, copy);
                    break;
                }

                case SceneBuffer buffer when buffer.Buffer is { } content:
                {
                    var copy = new SceneBuffer(target)
                    {
                        SourceBox = buffer.SourceBox,
                        DestinationWidth = buffer.DestinationWidth,
                        DestinationHeight = buffer.DestinationHeight,
                        IsOpaque = buffer.IsOpaque,
                        ColorDescription = buffer.ColorDescription,
                        TextureShader = buffer.TextureShader,
                    };
                    copy.SetPosition(buffer.X, buffer.Y);
                    copy.ClipBox = buffer.ClipBox;
                    copy.SetBuffer(content);
                    _nodes.Add(copy);
                    break;
                }

                case SceneRect rect:
                {
                    var copy = new SceneRect(target, rect.Width, rect.Height, rect.Color);
                    copy.SetPosition(rect.X, rect.Y);
                    copy.ClipBox = rect.ClipBox;
                    break;
                }
            }
        }
    }
}
