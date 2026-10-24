namespace Basin.SoftKeyboard;

/// <summary>
/// An immutable grid of <see cref="KeyCell"/> values in rows and columns. Each row is stretched to the full width on its own, and the row heights are relative.
/// </summary>
public sealed class KeyGrid
{
    /// <summary>
    /// The largest number of rows that a grid can have.
    /// </summary>
    public const int MaxRows = 8;
    /// <summary>
    /// The largest number of columns that a grid can have.
    /// </summary>
    public const int MaxColumns = 20;
    /// <summary>
    /// The largest number of cells that a grid can have. A buffer of this size fits every grid.
    /// </summary>
    public const int MaxCells = MaxRows * MaxColumns;
    /// <summary>
    /// The number of rows of a grid built without a shape.
    /// </summary>
    public const int DefaultRows = 5;
    /// <summary>
    /// The number of columns of a grid built without a shape.
    /// </summary>
    public const int DefaultColumns = 14;

    private static readonly float[] DefaultRowHeights = [55, 70, 70, 70, 70];

    private readonly KeyCell[] _cells;
    private readonly float[] _rowHeights;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyGrid"/> class with the LunaCE shape of <see cref="DefaultRows"/> by <see cref="DefaultColumns"/> cells and <see cref="RowBias"/> on.
    /// </summary>
    /// <param name="cells">The cells, row by row.</param>
    /// <param name="rowHeights">The relative row heights, or <see langword="null"/> for 55 for the number row and 70 for the others.</param>
    /// <param name="widthUnits">The width of the keyboard in key units.</param>
    /// <exception cref="ArgumentException">The number of cells or row heights does not match the shape.</exception>
    public KeyGrid(IReadOnlyList<KeyCell> cells, IReadOnlyList<float>? rowHeights = null, float widthUnits = 11)
        : this(DefaultRows, DefaultColumns, cells, rowHeights ?? DefaultRowHeights, widthUnits, rowBias: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyGrid"/> class with its own shape and <see cref="RowBias"/> off.
    /// </summary>
    /// <param name="rows">The number of rows, from 1 to <see cref="MaxRows"/>.</param>
    /// <param name="columns">The number of columns, from 1 to <see cref="MaxColumns"/>.</param>
    /// <param name="cells">The cells, row by row.</param>
    /// <param name="rowHeights">The relative row heights, or <see langword="null"/> for equal rows.</param>
    /// <param name="widthUnits">The width of the keyboard in key units.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> or <paramref name="columns"/> is out of range.</exception>
    /// <exception cref="ArgumentException">The number of cells or row heights does not match the shape.</exception>
    public KeyGrid(int rows, int columns, IReadOnlyList<KeyCell> cells, IReadOnlyList<float>? rowHeights = null, float widthUnits = 11)
        : this(rows, columns, cells, rowHeights, widthUnits, rowBias: false)
    {
    }

    private KeyGrid(int rows, int columns, IReadOnlyList<KeyCell> cells, IReadOnlyList<float>? rowHeights, float widthUnits, bool rowBias)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (rows is < 1 or > MaxRows)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), rows, $"a key grid has 1 to {MaxRows} rows");
        }

        if (columns is < 1 or > MaxColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), columns, $"a key grid has 1 to {MaxColumns} columns");
        }

        Rows = rows;
        Columns = columns;
        CellCount = rows * columns;
        if (cells.Count != CellCount)
        {
            throw new ArgumentException($"a {rows} x {columns} key grid has {CellCount} cells, not {cells.Count}", nameof(cells));
        }

        _cells = [.. cells];
        if (rowHeights is null)
        {
            _rowHeights = new float[rows];
            Array.Fill(_rowHeights, 70f);
        }
        else
        {
            _rowHeights = [.. rowHeights];
        }

        if (_rowHeights.Length != Rows)
        {
            throw new ArgumentException($"a key grid has {Rows} row heights", nameof(rowHeights));
        }

        WidthUnits = widthUnits;
        RowBias = rowBias;
    }

    /// <summary>
    /// Gets the number of rows.
    /// </summary>
    public int Rows { get; }

    /// <summary>
    /// Gets the number of columns.
    /// </summary>
    public int Columns { get; }

    /// <summary>
    /// Gets the number of cells, which is <see cref="Rows"/> times <see cref="Columns"/>.
    /// </summary>
    public int CellCount { get; }

    /// <summary>
    /// Gets a value indicating whether the hit test moves the effective centers of the number row and the bottom row. Only a grid without a shape has it.
    /// </summary>
    public bool RowBias { get; }

    /// <summary>
    /// Gets the width of the keyboard in key units, which is the sum of the bottom row.
    /// </summary>
    public float WidthUnits { get; }

    /// <summary>
    /// Gets the cells, row by row.
    /// </summary>
    public IReadOnlyList<KeyCell> Cells => _cells;

    /// <summary>
    /// Gets the relative row heights.
    /// </summary>
    public IReadOnlyList<float> RowHeights => _rowHeights;

    /// <summary>
    /// Gets the cell at a row and a column.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <returns>The cell.</returns>
    public KeyCell this[int row, int column] => _cells[(row * Columns) + column];

    /// <summary>
    /// Gets the cell at a cell index.
    /// </summary>
    /// <param name="index">The cell index, as <see cref="Index"/> gives it.</param>
    /// <returns>The cell.</returns>
    public KeyCell this[int index] => _cells[index];

    /// <summary>
    /// Gives the cell index of a row and a column.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <returns>The index into <see cref="Cells"/>.</returns>
    public int Index(int row, int column) => (row * Columns) + column;

    /// <summary>
    /// Gives the row of a cell index.
    /// </summary>
    /// <param name="index">The cell index.</param>
    /// <returns>The row index.</returns>
    public int RowOf(int index) => index / Columns;

    /// <summary>
    /// Gives the column of a cell index.
    /// </summary>
    /// <param name="index">The cell index.</param>
    /// <returns>The column index.</returns>
    public int ColumnOf(int index) => index % Columns;

    /// <summary>
    /// Gives the width of a row in key units: the sum of the absolute weights of its cells.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <returns>The row width in key units.</returns>
    public float RowWeight(int row)
    {
        var total = 0f;
        for (var column = 0; column < Columns; column++)
        {
            total += MathF.Abs(this[row, column].Weight);
        }

        return total;
    }

    /// <summary>
    /// Computes the rect of every cell without an allocation. A cell with a weight of 0 gets a rect with no width.
    /// </summary>
    /// <param name="width">The keyboard width in logical pixels.</param>
    /// <param name="keymapHeight">The height of the key rows in logical pixels.</param>
    /// <param name="rects">The caller's span, with at least <see cref="CellCount"/> rects. The rects are written by cell index.</param>
    /// <exception cref="ArgumentException"><paramref name="rects"/> is too short.</exception>
    public void Layout(float width, float keymapHeight, Span<KeyRect> rects)
    {
        if (rects.Length < CellCount)
        {
            throw new ArgumentException($"the span needs {CellCount} rects", nameof(rects));
        }

        Span<float> rowBottoms = stackalloc float[MaxRows];
        Span<float> columnRights = stackalloc float[MaxColumns];
        RowLimits(keymapHeight, rowBottoms);
        for (var row = 0; row < Rows; row++)
        {
            ColumnLimits(row, width, columnRights);
            var top = row > 0 ? rowBottoms[row - 1] : 0;
            for (var column = 0; column < Columns; column++)
            {
                var left = column > 0 ? columnRights[column - 1] : 0;
                rects[Index(row, column)] = new KeyRect(left, top, columnRights[column] - left, rowBottoms[row] - top);
            }
        }
    }

    /// <summary>
    /// Computes the bottom edge of each row.
    /// </summary>
    /// <param name="keymapHeight">The height of the key rows in logical pixels.</param>
    /// <param name="bottoms">The caller's span, with at least <see cref="Rows"/> entries.</param>
    public void RowLimits(float keymapHeight, Span<float> bottoms)
    {
        var total = 0f;
        for (var row = 0; row < Rows; row++)
        {
            total += _rowHeights[row];
        }

        var sum = 0f;
        for (var row = 0; row < Rows; row++)
        {
            sum += _rowHeights[row];
            bottoms[row] = sum * keymapHeight / total;
        }
    }

    /// <summary>
    /// Computes the right edge of each cell in a row. A row with no width gives 0 for every cell.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <param name="width">The keyboard width in logical pixels.</param>
    /// <param name="rights">The caller's span, with at least <see cref="Columns"/> entries.</param>
    public void ColumnLimits(int row, float width, Span<float> rights)
    {
        var total = RowWeight(row);
        var sum = 0f;
        for (var column = 0; column < Columns; column++)
        {
            sum += MathF.Abs(this[row, column].Weight);
            rights[column] = total > 0 ? sum * width / total : 0;
        }
    }

    /// <summary>
    /// Makes a copy of the grid with one cell replaced. The copy keeps the shape and <see cref="RowBias"/>.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <param name="cell">The new cell.</param>
    /// <returns>A new grid.</returns>
    public KeyGrid WithCell(int row, int column, KeyCell cell)
    {
        var cells = (KeyCell[])_cells.Clone();
        cells[Index(row, column)] = cell;
        return new KeyGrid(Rows, Columns, cells, _rowHeights, WidthUnits, RowBias);
    }
}
