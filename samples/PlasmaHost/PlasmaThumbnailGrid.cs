using Basin;
using Basin.Scene;

namespace PlasmaHost;

internal sealed class PlasmaThumbnailGrid : IDisposable
{
    private const int Gap = 24;
    private const int Label = 24;

    private readonly List<PlasmaThumbnail> _cells = [];
    private readonly SceneTree _tree;
    private bool _disposed;

    public PlasmaThumbnailGrid(SceneTree parent) => _tree = new SceneTree(parent) { Enabled = false };

    public IReadOnlyList<PlasmaThumbnail> Cells => _cells;

    public bool Visible
    {
        get => _tree.Enabled;
        set => _tree.Enabled = value;
    }

    public void RaiseToTop() => _tree.RaiseToTop();

    public void Layout(IReadOnlyList<PlasmaHostView> views, in Box area, Func<PlasmaHostView, Box> boxOf)
    {
        Clear();
        if (views.Count == 0 || area.IsEmpty)
        {
            return;
        }

        var columns = (int)Math.Ceiling(Math.Sqrt(views.Count));
        var rows = (int)Math.Ceiling(views.Count / (double)columns);
        var cellWidth = Math.Max(1, (area.Width - (Gap * (columns + 1))) / columns);
        var cellHeight = Math.Max(1, (area.Height - (Gap * (rows + 1))) / rows);

        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            var source = boxOf(view);
            var contentWidth = Math.Max(1, source.Width);
            var contentHeight = Math.Max(1, source.Height);
            var column = i % columns;
            var row = i / columns;
            var box = new Box(
                area.X + Gap + (column * (cellWidth + Gap)),
                area.Y + Gap + (row * (cellHeight + Gap)),
                cellWidth,
                cellHeight);

            var content = new Box(box.X, box.Y, box.Width, Math.Max(1, box.Height - Label));
            var cell = new PlasmaThumbnail(_tree, view, contentWidth, contentHeight);
            cell.Place(box, content, contentWidth, contentHeight, source.X - view.Tree.X, source.Y - view.Tree.Y);
            _cells.Add(cell);
        }
    }

    public PlasmaThumbnail? At(double x, double y)
    {
        foreach (var cell in _cells)
        {
            var box = cell.Box;
            if (x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom)
            {
                return cell;
            }
        }

        return null;
    }

    public void Clear()
    {
        foreach (var cell in _cells)
        {
            cell.Dispose();
        }

        _cells.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
        _tree.Destroy();
    }
}
