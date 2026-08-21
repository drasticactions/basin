using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Shell.River;

internal sealed class RiverBorders
{
    private readonly SceneTree _tree;
    private SceneRect? _top;
    private SceneRect? _bottom;
    private SceneRect? _left;
    private SceneRect? _right;

    internal RiverBorders(SceneTree parent) => _tree = new SceneTree(parent);

    internal SceneTree Tree => _tree;

    internal void Layout(ResizeEdges edges, int width, RenderColor color, in Box content, bool visible)
    {
        var on = visible && width > 0 && edges != ResizeEdges.None && !content.IsEmpty;
        _tree.Enabled = on;
        if (!on)
        {
            return;
        }

        var top = (edges & ResizeEdges.Top) != 0;
        var bottom = (edges & ResizeEdges.Bottom) != 0;
        var left = (edges & ResizeEdges.Left) != 0;
        var right = (edges & ResizeEdges.Right) != 0;

        var leftInset = left ? width : 0;
        var rightInset = right ? width : 0;

        Place(
            ref _top,
            top,
            new Box(content.X - leftInset, content.Y - width, content.Width + leftInset + rightInset, width),
            color);
        Place(
            ref _bottom,
            bottom,
            new Box(content.X - leftInset, content.Bottom, content.Width + leftInset + rightInset, width),
            color);
        Place(
            ref _left,
            left,
            new Box(content.X - width, content.Y, width, content.Height),
            color);
        Place(
            ref _right,
            right,
            new Box(content.Right, content.Y, width, content.Height),
            color);
    }

    internal void Destroy()
    {
        _top = null;
        _bottom = null;
        _left = null;
        _right = null;
        _tree.Destroy();
    }

    private void Place(ref SceneRect? rect, bool wanted, in Box box, RenderColor color)
    {
        if (!wanted || box.IsEmpty)
        {
            if (rect is not null)
            {
                rect.Enabled = false;
            }

            return;
        }

        rect ??= new SceneRect(_tree, box.Width, box.Height, color);
        rect.Enabled = true;
        rect.Width = box.Width;
        rect.Height = box.Height;
        rect.Color = color;
        rect.SetPosition(box.X, box.Y);
    }

    internal static RenderColor ToRenderColor(uint r, uint g, uint b, uint a) => new(
        (float)(r / (double)uint.MaxValue),
        (float)(g / (double)uint.MaxValue),
        (float)(b / (double)uint.MaxValue),
        (float)(a / (double)uint.MaxValue));
}
