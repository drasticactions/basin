using Basin.WindowManager;
using SkiaSharp;

namespace DeskbarWm;

internal sealed class WorkspacesApplet : IApplet
{
    private const int CellWidth = 23;
    private const int CellHeight = 14;

    private readonly List<(Rect Frame, uint Mask)> _windows = [];
    private int _rows = 2;
    private int _columns = 2;
    private int _current;
    private Rect _area;
    private Rect _lastRect;

    public string Name => "workspaces";

    public string RenderState
    {
        get
        {
            var key = $"{_rows}x{_columns}@{_current}";
            foreach (var (frame, mask) in _windows)
            {
                key += $"|{frame.X},{frame.Y},{frame.Width},{frame.Height},{mask}";
            }

            return key;
        }
    }

    public int PreferredHeight => (_rows * CellHeight) + 2;

    public void Update(WorkspaceGrid grid, Rect area, IReadOnlyList<ManagedWindow> windows)
    {
        _rows = grid.Rows;
        _columns = grid.Columns;
        _current = grid.Current;
        _area = area;
        _windows.Clear();
        foreach (var mw in windows)
        {
            if (!mw.Hidden && mw.Width > 0)
            {
                _windows.Add((mw.ContentRect, mw.WorkspaceMask));
            }
        }
    }

    public int MeasureWidth(SKFont font, int trayHeight) => (_columns * CellWidth) + 4;

    public int? CellAt(Point local)
    {
        var column = Math.Clamp((local.X - 2) / CellWidth, 0, _columns - 1);
        var row = Math.Clamp((local.Y - 1) / CellHeight, 0, _rows - 1);
        var index = (row * _columns) + column;
        return index >= 0 && index < _rows * _columns ? index : null;
    }

    public void Draw(SKCanvas canvas, SKPaint paint, SKFont font, Rect rect)
    {
        _lastRect = rect;
        var panel = new SKColor(216, 216, 216);
        var originX = rect.X + 2;
        var originY = rect.Y + ((rect.Height - (_rows * CellHeight)) / 2);
        for (var row = 0; row < _rows; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                var index = (row * _columns) + column;
                var cell = new Rect(
                    originX + (column * CellWidth),
                    originY + (row * CellHeight),
                    CellWidth,
                    CellHeight);

                paint.Color = index == _current
                    ? Theme.Tint(panel, Theme.LightenHalf)
                    : Theme.Tint(panel, Theme.DarkenHalf);
                canvas.DrawRect(cell.X, cell.Y, cell.Width - 1, cell.Height - 1, paint);

                if (!_area.IsEmpty)
                {
                    paint.Color = new SKColor(120, 140, 170);
                    foreach (var (frame, mask) in _windows)
                    {
                        if (!Workspace.Includes(mask, index))
                        {
                            continue;
                        }

                        var x = cell.X + ((frame.X - _area.X) * (cell.Width - 1) / Math.Max(_area.Width, 1));
                        var y = cell.Y + ((frame.Y - _area.Y) * (cell.Height - 1) / Math.Max(_area.Height, 1));
                        var w = Math.Max(frame.Width * (cell.Width - 1) / Math.Max(_area.Width, 1), 2);
                        var h = Math.Max(frame.Height * (cell.Height - 1) / Math.Max(_area.Height, 1), 2);
                        canvas.DrawRect(
                            Math.Clamp(x, cell.X, cell.Right - 2),
                            Math.Clamp(y, cell.Y, cell.Bottom - 2),
                            Math.Min(w, cell.Width - 2),
                            Math.Min(h, cell.Height - 2),
                            paint);
                    }
                }

                paint.Color = index == _current ? SKColors.Black : Theme.Tint(panel, Theme.Darken2);
                canvas.DrawRect(cell.X, cell.Y, cell.Width - 1, 1, paint);
                canvas.DrawRect(cell.X, cell.Bottom - 2, cell.Width - 1, 1, paint);
                canvas.DrawRect(cell.X, cell.Y, 1, cell.Height - 1, paint);
                canvas.DrawRect(cell.Right - 2, cell.Y, 1, cell.Height - 1, paint);
            }
        }
    }

    public Rect LastRect => _lastRect;
}
