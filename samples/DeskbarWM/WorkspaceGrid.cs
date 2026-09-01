namespace DeskbarWm;

internal sealed class WorkspaceGrid
{
    public int Rows { get; private set; } = 2;

    public int Columns { get; private set; } = 2;

    public int Count => Math.Min(Rows * Columns, 32);

    public int Current { get; private set; }

    public int Previous { get; private set; }

    public void Configure(int rows, int columns)
    {
        Rows = Math.Clamp(rows, 1, 8);
        Columns = Math.Clamp(columns, 1, 8);
        if (Current >= Count)
        {
            Current = 0;
        }

        if (Previous >= Count)
        {
            Previous = 0;
        }
    }

    public bool SwitchTo(int index)
    {
        if (index < 0 || index >= Count || index == Current)
        {
            return false;
        }

        Previous = Current;
        Current = index;
        return true;
    }

    public int Moved(int dx, int dy)
    {
        var row = Current / Columns;
        var column = Current % Columns;
        row = ((row + dy) % Rows + Rows) % Rows;
        column = ((column + dx) % Columns + Columns) % Columns;
        var index = (row * Columns) + column;
        return index < Count ? index : Current;
    }
}
