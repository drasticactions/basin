using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

public class SceneTree : SceneNode
{
    public SceneTree(SceneTree? parent)
        : base(parent)
    {
    }

    public List<SceneNode> Children { get; } = [];

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

    protected override void OnDestroy()
    {
        while (Children.Count > 0)
        {
            Children[^1].Destroy();
        }
    }
}
