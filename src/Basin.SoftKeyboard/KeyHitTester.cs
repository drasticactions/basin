namespace Basin.SoftKeyboard;

public sealed class KeyHitTester
{
    private readonly float[] _columnRights = new float[KeyGrid.MaxCells];
    private readonly float[] _rowBottoms = new float[KeyGrid.MaxRows];
    private readonly KeyRect[] _rects = new KeyRect[KeyGrid.MaxCells];
    private KeyGrid _grid;
    private float _width;
    private float _height;

    public KeyHitTester(KeyGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _grid = grid;
    }

    public KeyGrid Grid
    {
        get => _grid;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _grid = value;
            Layout(_width, _height);
        }
    }

    public float Width => _width;

    public float Height => _height;

    public ReadOnlySpan<KeyRect> Rects => _rects.AsSpan(0, _grid.CellCount);

    public KeyRect RectOf(int cell) => _rects[cell];

    public void Layout(float width, float keymapHeight)
    {
        _width = width;
        _height = keymapHeight;
        _grid.RowLimits(keymapHeight, _rowBottoms);
        for (var row = 0; row < _grid.Rows; row++)
        {
            _grid.ColumnLimits(row, width, _columnRights.AsSpan(row * _grid.Columns, _grid.Columns));
        }

        _grid.Layout(width, keymapHeight, _rects);
    }

    public int Find(float x, float y, bool diamonds = true)
    {
        var row = 0;
        while (y > _rowBottoms[row] && ++row < _grid.Rows)
        {
        }

        if (row >= _grid.Rows)
        {
            return -1;
        }

        var column = 0;
        while (x > Right(row, column) && ++column < _grid.Columns)
        {
        }

        if (column >= _grid.Columns)
        {
            return -1;
        }

        var cell = _grid[row, column];
        if (cell.IsSpacer)
        {
            return -1;
        }

        var changed = false;
        if (diamonds)
        {
            var centerY = CenterOfRow(row, cell.Plain);
            var min = (_rowBottoms[row] - (row > 0 ? _rowBottoms[row - 1] : 0)) / 10;
            var otherRow = -1;
            if (row > 0 && y < centerY - min)
            {
                otherRow = row - 1;
            }
            else if (row < _grid.Rows - 1 && y > centerY + min)
            {
                otherRow = row + 1;
            }

            if (otherRow >= 0)
            {
                var otherColumn = column;
                while (otherColumn > 0 && x < Right(otherRow, otherColumn))
                {
                    otherColumn--;
                }

                while (x > Right(otherRow, otherColumn) && ++otherColumn < _grid.Columns)
                {
                }

                if (otherColumn < _grid.Columns)
                {
                    var centerX = CenterOfKey(x, row, column, cell.Weight);
                    var other = _grid[otherRow, otherColumn];
                    var centerOtherX = CenterOfKey(x, otherRow, otherColumn, other.Weight);
                    var centerOtherY = CenterOfRow(otherRow, other.Plain);
                    var first = Square(y - centerY);
                    var second = Square(y - centerOtherY);
                    if (MathF.Abs(centerX - centerOtherX) > 2)
                    {
                        first += Square(x - centerX);
                        second += Square(x - centerOtherX);
                    }

                    if (other.IsKey && second * AdjustedWeight(cell) < first * AdjustedWeight(other))
                    {
                        row = otherRow;
                        column = otherColumn;
                        cell = other;
                        changed = true;
                    }
                }
            }
        }

        if (!changed && cell.IsInvisible)
        {
            for (var nearColumn = Math.Max(0, column - 1); nearColumn <= column + 1 && nearColumn < _grid.Columns; nearColumn++)
            {
                for (var nearRow = Math.Max(0, row - 1); nearRow <= row + 1 && nearRow < _grid.Rows; nearRow++)
                {
                    if ((nearColumn != column || nearRow != row) && _grid[nearRow, nearColumn] is { IsVisible: true } near && SameCap(near.Plain, cell.Plain))
                    {
                        return _grid.Index(nearRow, nearColumn);
                    }
                }
            }
        }

        return cell.IsKey ? _grid.Index(row, column) : -1;
    }

    internal static bool SameCap(KeyCap? a, KeyCap? b) =>
        a is not null && b is not null && a.Function == b.Function && a.Text == b.Text;

    private float Right(int row, int column) => _columnRights[(row * _grid.Columns) + column];

    private float Left(int row, int column) => column > 0 ? Right(row, column - 1) : 0;

    private float CenterOfKey(float touchX, int row, int column, float weight)
    {
        var left = Left(row, column);
        var right = Right(row, column);
        var center = (left + right) / 2;
        if (weight > 1)
        {
            var radius = (right - left) / (weight * 2);
            if (touchX < center)
            {
                var leftMost = left + radius;
                center = touchX < leftMost ? leftMost : touchX;
            }
            else
            {
                var rightMost = right - radius;
                center = touchX > rightMost ? rightMost : touchX;
            }
        }

        return center;
    }

    private float CenterOfRow(int row, KeyCap? cap)
    {
        var top = row > 0 ? _rowBottoms[row - 1] : 0;
        var bottom = _rowBottoms[row];
        if (!_grid.RowBias)
        {
            return (top + bottom) / 2;
        }

        if (row == 0)
        {
            return bottom / 4;
        }

        if (row < _grid.Rows - 1)
        {
            return (top + bottom) / 2;
        }

        return cap is { IsSpace: true }
            ? (top + bottom) / 2
            : (top + (2 * bottom)) / 3;
    }

    private static float AdjustedWeight(KeyCell cell)
    {
        if (cell.Weight < 1)
        {
            return 0.3f;
        }

        return cell.Plain switch
        {
            { Function: KeyFunction.Hide } => 0.4f,
            { Function: KeyFunction.Tab } => 0.7f,
            { IsSpace: true } => 0.9f,
            _ => 1.0f,
        };
    }

    private static float Square(float value) => value * value;
}
