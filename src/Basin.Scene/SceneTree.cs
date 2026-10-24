using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// A node that groups child nodes. Children draw in list order, so the last child is on top.
/// </summary>
public class SceneTree : SceneNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SceneTree"/> class.
    /// </summary>
    /// <param name="parent">The tree to add this tree to, or <see langword="null"/> for a detached root.</param>
    public SceneTree(SceneTree? parent)
        : base(parent)
    {
    }

    /// <summary>
    /// Gets the child nodes from bottom to top. Use the stacking methods on <see cref="SceneNode"/> to change the list, because they also damage the scene.
    /// </summary>
    public List<SceneNode> Children { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the nodes in this subtree must always be composited, and never put on a plane or scanned out directly.
    /// </summary>
    public bool ExcludeFromScanout { get; set; }

    private float _alpha = 1f;

    /// <summary>
    /// Gets or sets the opacity that multiplies every node in the subtree, from 0 to 1.
    /// </summary>
    public float Alpha
    {
        get => _alpha;
        set
        {
            if (_alpha != value)
            {
                DamageSubtree();
                _alpha = value;
                DamageSubtree();
            }
        }
    }

    private TextureFilter? _filter;

    /// <summary>
    /// Gets or sets the texture filter for the subtree. The nearest ancestor with a filter wins. With none, the filter is <see cref="TextureFilter.Linear"/>.
    /// </summary>
    public TextureFilter? Filter
    {
        get => _filter;
        set
        {
            if (_filter != value)
            {
                _filter = value;
                DamageSubtree();
            }
        }
    }

    internal Scene? Owner { get; set; }

    internal List<SceneMirror>? Mirrors { get; set; }

    internal override Box SubtreeBounds()
    {
        var any = false;
        int minX = 0, minY = 0, maxX = 0, maxY = 0;
        foreach (var child in Children)
        {
            if (!child.Enabled)
            {
                continue;
            }

            var bounds = child.SubtreeBounds();
            if (bounds.IsEmpty)
            {
                continue;
            }

            var x0 = bounds.X + child.X;
            var y0 = bounds.Y + child.Y;
            var x1 = x0 + bounds.Width;
            var y1 = y0 + bounds.Height;
            if (!any)
            {
                (minX, minY, maxX, maxY) = (x0, y0, x1, y1);
                any = true;
            }
            else
            {
                minX = Math.Min(minX, x0);
                minY = Math.Min(minY, y0);
                maxX = Math.Max(maxX, x1);
                maxY = Math.Max(maxY, y1);
            }
        }

        if (!any)
        {
            return default;
        }

        var union = new Box(minX, minY, maxX - minX, maxY - minY);
        return IsClipped ? union.Intersect(ClipBox) : union;
    }

    internal override void DamageInto(Scene scene, int sceneX, int sceneY)
    {
        foreach (var child in Children)
        {
            if (child.Enabled)
            {
                child.DamageInto(scene, sceneX + child.X, sceneY + child.Y);
            }
        }
    }

    /// <summary>
    /// Destroys every child, from the top down.
    /// </summary>
    protected override void OnDestroy()
    {
        while (Children.Count > 0)
        {
            Children[^1].Destroy();
        }
    }
}
