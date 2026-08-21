using Basin.WindowManager;

namespace RetroWm;

internal sealed class OutputGrid
{
    public const double MinimumFraction = 0.05;

    private readonly List<List<ManagedWindow>> _columns = [];

    public List<double> ColumnFractions { get; } = [];

    public List<List<double>> RowFractions { get; } = [];

    public List<ManagedWindow> Tiles { get; } = [];

    public List<(int Column, int Row)> Cells { get; } = [];

    public int ColumnCount => _columns.Count;

    public int Count
    {
        get
        {
            var count = 0;
            foreach (var column in _columns)
            {
                count += column.Count;
            }

            return count;
        }
    }

    public int WindowsInColumn(int column) => _columns[column].Count;

    public bool Contains(ManagedWindow mw)
    {
        foreach (var column in _columns)
        {
            if (column.Contains(mw))
            {
                return true;
            }
        }

        return false;
    }

    public void Add(ManagedWindow mw)
    {
        if (Contains(mw))
        {
            return;
        }

        var n = Count + 1;
        if (_columns.Count < (int)Math.Ceiling(Math.Sqrt(n)))
        {
            _columns.Add([mw]);
            return;
        }

        var best = 0;
        for (var col = 1; col < _columns.Count; col++)
        {
            if (_columns[col].Count < _columns[best].Count)
            {
                best = col;
            }
        }

        _columns[best].Add(mw);
    }

    public void Remove(ManagedWindow mw)
    {
        for (var col = 0; col < _columns.Count; col++)
        {
            if (_columns[col].Remove(mw))
            {
                if (_columns[col].Count == 0)
                {
                    _columns.RemoveAt(col);
                }

                return;
            }
        }
    }

    public (int Column, int Row) PositionOf(ManagedWindow mw)
    {
        for (var col = 0; col < _columns.Count; col++)
        {
            var row = _columns[col].IndexOf(mw);
            if (row >= 0)
            {
                return (col, row);
            }
        }

        return (-1, -1);
    }

    public void Swap(ManagedWindow a, ManagedWindow b)
    {
        var (colA, rowA) = PositionOf(a);
        var (colB, rowB) = PositionOf(b);
        if (colA < 0 || colB < 0)
        {
            return;
        }

        _columns[colA][rowA] = b;
        _columns[colB][rowB] = a;
    }

    public void SplitBeside(ManagedWindow mw, ManagedWindow target, bool after)
    {
        if (ReferenceEquals(mw, target) || PositionOf(target).Column < 0)
        {
            return;
        }

        Remove(mw);
        var (targetColumn, _) = PositionOf(target);
        if (targetColumn < 0)
        {
            Add(mw);
            return;
        }

        _columns.Insert(after ? targetColumn + 1 : targetColumn, [mw]);
    }

    public void StackOn(ManagedWindow mw, ManagedWindow target, bool below)
    {
        if (ReferenceEquals(mw, target) || PositionOf(target).Column < 0)
        {
            return;
        }

        Remove(mw);
        var (targetColumn, targetRow) = PositionOf(target);
        if (targetColumn < 0)
        {
            Add(mw);
            return;
        }

        _columns[targetColumn].Insert(below ? targetRow + 1 : targetRow, mw);
    }

    public void SplitAtEdge(ManagedWindow mw, bool after)
    {
        var (column, _) = PositionOf(mw);
        if (column < 0 || (_columns[column].Count == 1
            && (after ? column == _columns.Count - 1 : column == 0)))
        {
            return;
        }

        Remove(mw);
        _columns.Insert(after ? _columns.Count : 0, [mw]);
    }

    public void MoveIntoColumn(ManagedWindow mw, int targetColumn, int nearRow)
    {
        var (column, _) = PositionOf(mw);
        if (column < 0 || targetColumn < 0 || targetColumn >= _columns.Count
            || column == targetColumn)
        {
            return;
        }

        var target = _columns[targetColumn];
        Remove(mw);
        target.Insert(Math.Clamp(nearRow, 0, target.Count), mw);
    }

    public void ReorderInColumn(ManagedWindow mw, int delta)
    {
        var (column, row) = PositionOf(mw);
        if (column < 0)
        {
            return;
        }

        var rows = _columns[column];
        var targetRow = row + delta;
        if (targetRow < 0 || targetRow >= rows.Count)
        {
            return;
        }

        (rows[row], rows[targetRow]) = (rows[targetRow], rows[row]);
    }

    public Rect? PreviewDrop(ManagedWindow mw, ManagedWindow target, DropKind kind, Rect area)
    {
        if (ReferenceEquals(mw, target) || PositionOf(mw).Column < 0)
        {
            return null;
        }

        if (kind == DropKind.Swap)
        {
            var index = CellIndexOf(target);
            return index >= 0 && index < Cells.Count ? FrameFor(index, area) : null;
        }

        var columns = new List<List<ManagedWindow>>(_columns.Count + 1);
        foreach (var column in _columns)
        {
            columns.Add(new List<ManagedWindow>(column));
        }

        for (var col = 0; col < columns.Count; col++)
        {
            if (columns[col].Remove(mw))
            {
                if (columns[col].Count == 0)
                {
                    columns.RemoveAt(col);
                }

                break;
            }
        }

        var targetColumn = -1;
        var targetRow = -1;
        for (var col = 0; col < columns.Count; col++)
        {
            var row = columns[col].IndexOf(target);
            if (row >= 0)
            {
                targetColumn = col;
                targetRow = row;
                break;
            }
        }

        if (targetColumn < 0)
        {
            return null;
        }

        switch (kind)
        {
            case DropKind.StackAbove:
                columns[targetColumn].Insert(targetRow, mw);
                break;
            case DropKind.StackBelow:
                columns[targetColumn].Insert(targetRow + 1, mw);
                break;
            case DropKind.SplitLeft:
                columns.Insert(targetColumn, [mw]);
                break;
            default:
                columns.Insert(targetColumn + 1, [mw]);
                break;
        }

        var columnFractions = ColumnFractions.Count == columns.Count
            ? new List<double>(ColumnFractions)
            : EqualRows(columns.Count);
        var mwColumn = -1;
        var mwRow = -1;
        for (var col = 0; col < columns.Count; col++)
        {
            var row = columns[col].IndexOf(mw);
            if (row >= 0)
            {
                mwColumn = col;
                mwRow = row;
                break;
            }
        }

        if (mwColumn < 0)
        {
            return null;
        }

        var rows = ColumnFractions.Count == columns.Count
            && mwColumn < RowFractions.Count
            && RowFractions[mwColumn].Count == columns[mwColumn].Count
            ? RowFractions[mwColumn]
            : EqualRows(columns[mwColumn].Count);

        var x0 = Boundary(area.X, area.Width, columnFractions, mwColumn);
        var x1 = Boundary(area.X, area.Width, columnFractions, mwColumn + 1);
        var y0 = Boundary(area.Y, area.Height, rows, mwRow);
        var y1 = Boundary(area.Y, area.Height, rows, mwRow + 1);
        return new Rect(x0, y0, Math.Max(x1 - x0, 1), Math.Max(y1 - y0, 1));
    }

    public int CellIndexOf(ManagedWindow mw)
    {
        for (var i = 0; i < Tiles.Count; i++)
        {
            if (ReferenceEquals(Tiles[i], mw))
            {
                return i;
            }
        }

        return -1;
    }

    public void EnsureFractions()
    {
        for (var col = _columns.Count - 1; col >= 0; col--)
        {
            if (_columns[col].Count == 0)
            {
                _columns.RemoveAt(col);
            }
        }

        if (_columns.Count == 0)
        {
            ColumnFractions.Clear();
            RowFractions.Clear();
            Tiles.Clear();
            Cells.Clear();
            return;
        }

        if (ColumnFractions.Count != _columns.Count)
        {
            ColumnFractions.Clear();
            RowFractions.Clear();
            for (var col = 0; col < _columns.Count; col++)
            {
                ColumnFractions.Add(1.0 / _columns.Count);
                RowFractions.Add(EqualRows(_columns[col].Count));
            }
        }
        else
        {
            for (var col = 0; col < _columns.Count; col++)
            {
                if (RowFractions[col].Count != _columns[col].Count)
                {
                    RowFractions[col] = EqualRows(_columns[col].Count);
                }
            }
        }

        Tiles.Clear();
        Cells.Clear();
        for (var col = 0; col < _columns.Count; col++)
        {
            for (var row = 0; row < _columns[col].Count; row++)
            {
                Tiles.Add(_columns[col][row]);
                Cells.Add((col, row));
            }
        }
    }

    public Rect FrameFor(int index, Rect area)
    {
        var (col, row) = Cells[index];
        var x0 = Boundary(area.X, area.Width, ColumnFractions, col);
        var x1 = Boundary(area.X, area.Width, ColumnFractions, col + 1);
        var rows = RowFractions[col];
        var y0 = Boundary(area.Y, area.Height, rows, row);
        var y1 = Boundary(area.Y, area.Height, rows, row + 1);
        return new Rect(x0, y0, Math.Max(x1 - x0, 1), Math.Max(y1 - y0, 1));
    }

    public static void NudgeBoundary(List<double> fractions, int boundary, double shift)
    {
        if (boundary <= 0 || boundary >= fractions.Count)
        {
            return;
        }

        var low = -(fractions[boundary - 1] - MinimumFraction);
        var high = fractions[boundary] - MinimumFraction;
        shift = Math.Clamp(shift, Math.Min(low, 0), Math.Max(high, 0));
        fractions[boundary - 1] += shift;
        fractions[boundary] -= shift;
    }

    public static void ShiftBoundary(List<double> fractions, double[] start, int boundary, double shift)
    {
        if (boundary <= 0 || boundary >= fractions.Count)
        {
            return;
        }

        var low = -(start[boundary - 1] - MinimumFraction);
        var high = start[boundary] - MinimumFraction;
        shift = Math.Clamp(shift, Math.Min(low, 0), Math.Max(high, 0));
        fractions[boundary - 1] = start[boundary - 1] + shift;
        fractions[boundary] = start[boundary] - shift;
    }

    private static int Boundary(int origin, int extent, List<double> fractions, int index)
    {
        var sum = 0.0;
        for (var i = 0; i < index; i++)
        {
            sum += fractions[i];
        }

        return origin + (int)Math.Round(Math.Min(sum, 1.0) * extent);
    }

    private static List<double> EqualRows(int count)
    {
        var rows = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(1.0 / count);
        }

        return rows;
    }
}
