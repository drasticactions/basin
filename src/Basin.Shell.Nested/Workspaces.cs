namespace Basin.Shell.Nested;

public sealed class Workspaces
{
    private readonly IReadOnlyList<string> _givenNames;
    private readonly int _requestedRows;

    public Workspaces(int count, int rows, IReadOnlyList<string> names)
    {
        _givenNames = names;
        _requestedRows = Math.Max(1, rows);
        Count = Math.Max(1, count);
        Names = BuildNames();
    }

    public int Count { get; private set; }

    public int Rows => Math.Min(_requestedRows, Count);

    public int Columns => (Count + Rows - 1) / Rows;

    public int Current { get; private set; }

    public IReadOnlyList<string> Names { get; private set; }

    public void Switch(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        Current = index;
    }

    public int? Neighbor(WorkspaceDirection direction, WrapStyle wrap = WrapStyle.NoWrap)
    {
        var rows = Rows;
        var cols = Columns;
        var row = Current / cols;
        var col = Current % cols;
        switch (direction)
        {
            case WorkspaceDirection.Left:
                col--;
                break;
            case WorkspaceDirection.Right:
                col++;
                break;
            case WorkspaceDirection.Up:
                row--;
                break;
            case WorkspaceDirection.Down:
                row++;
                break;
        }
        if (col < 0)
        {
            if (wrap == WrapStyle.Classic)
                row = row > 0 ? row - 1 : rows - 1;
            col = wrap == WrapStyle.NoWrap ? 0 : cols - 1;
        }
        if (col >= cols)
        {
            if (wrap == WrapStyle.Classic)
                row = row < rows - 1 ? row + 1 : 0;
            col = wrap == WrapStyle.NoWrap ? cols - 1 : 0;
        }
        if (row < 0)
        {
            if (wrap == WrapStyle.Classic)
                col = col > 0 ? col - 1 : cols - 1;
            row = wrap == WrapStyle.NoWrap ? 0 : rows - 1;
        }
        if (row >= rows)
        {
            if (wrap == WrapStyle.Classic)
                col = col < cols - 1 ? col + 1 : 0;
            row = wrap == WrapStyle.NoWrap ? rows - 1 : 0;
        }
        if (wrap != WrapStyle.NoWrap && row * cols + col >= Count)
        {
            switch (direction)
            {
                case WorkspaceDirection.Left:
                    col = Count - (row * cols + 1);
                    break;
                case WorkspaceDirection.Right:
                    col = 0;
                    if (wrap == WrapStyle.Classic)
                        row = 0;
                    break;
                case WorkspaceDirection.Up:
                    row--;
                    break;
                case WorkspaceDirection.Down:
                    row = 0;
                    if (wrap == WrapStyle.Classic)
                        col = col < cols - 1 ? col + 1 : 0;
                    break;
            }
        }
        var index = row * cols + col;
        if (index < 0 || index >= Count || index == Current)
            return null;
        return index;
    }

    public Range Resize(int count)
    {
        var previous = Count;
        Count = Math.Max(1, count);
        Names = BuildNames();
        if (Current >= Count)
            Current = Count - 1;
        return Count < previous ? Count..previous : Count..Count;
    }

    private string[] BuildNames()
    {
        var names = new string[Count];
        for (var i = 0; i < Count; i++)
            names[i] = i < _givenNames.Count ? _givenNames[i] : (i + 1).ToString();
        return names;
    }
}
