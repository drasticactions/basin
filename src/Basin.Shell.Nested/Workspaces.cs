namespace Basin.Shell.Nested;

/// <summary>
/// The grid of workspaces, the current one, and their names. It always has at least one workspace.
/// </summary>
public sealed class Workspaces
{
    private readonly IReadOnlyList<string> _givenNames;
    private readonly int _requestedRows;

    /// <summary>
    /// Initializes a new instance of the <see cref="Workspaces"/> class. The current workspace is the first.
    /// </summary>
    /// <param name="count">The number of workspaces. A value below 1 counts as 1.</param>
    /// <param name="rows">The number of grid rows. A value below 1 counts as 1.</param>
    /// <param name="names">The names, in index order. The instance keeps this list and numbers the workspaces that it does not name.</param>
    public Workspaces(int count, int rows, IReadOnlyList<string> names)
    {
        _givenNames = names;
        _requestedRows = Math.Max(1, rows);
        Count = Math.Max(1, count);
        Names = BuildNames();
    }

    /// <summary>
    /// Gets the number of workspaces.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the number of grid rows. It is never more than <see cref="Count"/>.
    /// </summary>
    public int Rows => Math.Min(_requestedRows, Count);

    /// <summary>
    /// Gets the number of grid columns. The last row can be shorter than the others.
    /// </summary>
    public int Columns => (Count + Rows - 1) / Rows;

    /// <summary>
    /// Gets the zero-based index of the current workspace.
    /// </summary>
    public int Current { get; private set; }

    /// <summary>
    /// Gets one name per workspace. A workspace with no configured name has its one-based number as its name.
    /// </summary>
    public IReadOnlyList<string> Names { get; private set; }

    /// <summary>
    /// Makes a workspace current.
    /// </summary>
    /// <param name="index">The zero-based workspace index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public void Switch(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        Current = index;
    }

    /// <summary>
    /// Finds the workspace next to the current one on the grid.
    /// </summary>
    /// <param name="direction">The direction to step.</param>
    /// <param name="wrap">What a step past the edge of the grid does.</param>
    /// <returns>The index of the neighbor, or <see langword="null"/> when the step stays on the current workspace.</returns>
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

    /// <summary>
    /// Changes the number of workspaces and renames them. The current workspace moves to the last one when it is removed.
    /// </summary>
    /// <param name="count">The new number of workspaces. A value below 1 counts as 1.</param>
    /// <returns>The range of indices that were removed. It is empty when the count did not get smaller.</returns>
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
