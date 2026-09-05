using Basin.Diagnostics;

namespace Basin.Scene;

public sealed class SceneSnapshot : IDisposable
{
    private readonly List<SceneBuffer> _nodes = [];
    private bool _destroyed;

    private SceneSnapshot(SceneTree tree)
    {
        Tree = tree;
        BasinCounters.Track();
    }

    public static SceneSnapshot Capture(SceneSurface source, SceneTree parent)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Capture(source.Tree, parent);
    }

    public static SceneSnapshot Capture(SceneTree source, SceneTree parent)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parent);

        var tree = new SceneTree(parent);
        tree.SetPosition(source.X, source.Y);
        tree.ClipBox = source.ClipBox;

        var snapshot = new SceneSnapshot(tree);
        snapshot.CopyTree(source, tree);
        return snapshot;
    }

    public SceneTree Tree { get; }

    public int NodeCount => _nodes.Count;

    public bool IsDestroyed => _destroyed;

    public void Destroy()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        foreach (var node in _nodes)
        {
            node.SetBuffer(null);
        }

        _nodes.Clear();
        Tree.Destroy();
        BasinCounters.Untrack();
    }

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
                case SceneTree subtree:
                {
                    var copy = new SceneTree(target);
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
