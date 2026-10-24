namespace Basin.WindowManager;

/// <summary>
/// The layout and hover state of a menu, with no pixels. The painter sets each row's height and <see cref="Content"/>, and this type hit-tests and steps the selection.
/// </summary>
public sealed class WmMenuModel
{
    private readonly int[] _heights;
    private readonly bool[] _selectable;
    private (int Scale, int Hovered)? _rendered;

    /// <summary>
    /// Initializes a new instance of the <see cref="WmMenuModel"/> class. Every row starts with zero height and can be selected.
    /// </summary>
    /// <param name="rows">The number of rows, which never changes.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> is negative.</exception>
    public WmMenuModel(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        _heights = new int[rows];
        _selectable = new bool[rows];
        Array.Fill(_selectable, true);
    }

    /// <summary>
    /// Gets the number of rows.
    /// </summary>
    public int Count => _heights.Length;

    /// <summary>
    /// Gets or sets the box that the rows fill, in menu-local logical pixels. A border, shadow or header stays outside it.
    /// </summary>
    public Rect Content { get; set; }

    /// <summary>
    /// Gets the hovered row, or -1 when no row is hovered.
    /// </summary>
    public int Hovered { get; private set; } = -1;

    /// <summary>
    /// Gets the height of a row.
    /// </summary>
    /// <param name="row">The zero-based row index.</param>
    /// <returns>The row height in logical pixels.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="row"/> is out of range.</exception>
    public int Height(int row) => _heights[row];

    /// <summary>
    /// Sets a row's height and whether it can be selected.
    /// </summary>
    /// <param name="row">The zero-based row index.</param>
    /// <param name="height">The height in logical pixels. A negative value becomes zero.</param>
    /// <param name="selectable"><see langword="false"/> for a row that hover and steps skip, for example a separator.</param>
    /// <exception cref="IndexOutOfRangeException"><paramref name="row"/> is out of range.</exception>
    public void SetRow(int row, int height, bool selectable = true)
    {
        _heights[row] = Math.Max(height, 0);
        _selectable[row] = selectable;
    }

    /// <summary>
    /// Sets whether a row can be selected, without a change to its height.
    /// </summary>
    /// <param name="row">The zero-based row index.</param>
    /// <param name="selectable"><see langword="false"/> for a row that hover and steps skip.</param>
    /// <exception cref="IndexOutOfRangeException"><paramref name="row"/> is out of range.</exception>
    public void SetSelectable(int row, bool selectable) => _selectable[row] = selectable;

    /// <summary>
    /// Tests whether a row can be selected.
    /// </summary>
    /// <param name="row">The zero-based row index.</param>
    /// <returns><see langword="true"/> for a selectable row in range. It is <see langword="false"/> for an index out of range.</returns>
    public bool IsSelectable(int row) => row >= 0 && row < Count && _selectable[row];

    /// <summary>
    /// Adds the heights of all rows.
    /// </summary>
    /// <returns>The total row height in logical pixels.</returns>
    public int RowsHeight()
    {
        var total = 0;
        foreach (var height in _heights)
        {
            total += height;
        }

        return total;
    }

    /// <summary>
    /// Finds the row under a point. Rows stack down from the top of <see cref="Content"/>.
    /// </summary>
    /// <param name="x">The horizontal coordinate in menu-local logical pixels.</param>
    /// <param name="y">The vertical coordinate in menu-local logical pixels.</param>
    /// <returns>The row index, or <see langword="null"/> when the point is outside the content width, above it, or below the last row.</returns>
    public int? RowAt(int x, int y)
    {
        if (x < Content.X || x >= Content.X + Content.Width || y < Content.Y)
        {
            return null;
        }

        var offset = Content.Y;
        for (var i = 0; i < _heights.Length; i++)
        {
            if (y < offset + _heights[i])
            {
                return i;
            }

            offset += _heights[i];
        }

        return null;
    }

    /// <summary>
    /// Finds the selectable row under a point.
    /// </summary>
    /// <param name="x">The horizontal coordinate in menu-local logical pixels.</param>
    /// <param name="y">The vertical coordinate in menu-local logical pixels.</param>
    /// <returns>The row index, or <see langword="null"/> when no row or a row that cannot be selected is there.</returns>
    public int? SelectableAt(int x, int y) => RowAt(x, y) is { } row && _selectable[row] ? row : null;

    /// <summary>
    /// Gets the box of a row, as wide as <see cref="Content"/>.
    /// </summary>
    /// <param name="row">The zero-based row index.</param>
    /// <returns>The row's box in menu-local logical pixels.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="row"/> is out of range.</exception>
    public Rect RowRect(int row)
    {
        var offset = Content.Y;
        for (var i = 0; i < row; i++)
        {
            offset += _heights[i];
        }

        return new Rect(Content.X, offset, Content.Width, _heights[row]);
    }

    /// <summary>
    /// Hovers the selectable row under a point, or no row when there is none.
    /// </summary>
    /// <param name="x">The horizontal coordinate in menu-local logical pixels.</param>
    /// <param name="y">The vertical coordinate in menu-local logical pixels.</param>
    /// <returns><see langword="true"/> when <see cref="Hovered"/> changed.</returns>
    public bool Hover(int x, int y) => SetHovered(SelectableAt(x, y) ?? -1);

    /// <summary>
    /// Hovers no row.
    /// </summary>
    /// <returns><see langword="true"/> when a row was hovered before.</returns>
    public bool ClearHover() => SetHovered(-1);

    /// <summary>
    /// Sets the hovered row. It does not check the index or whether the row can be selected.
    /// </summary>
    /// <param name="row">The row index, or -1 for no row.</param>
    /// <returns><see langword="true"/> when <see cref="Hovered"/> changed.</returns>
    public bool SetHovered(int row)
    {
        if (row == Hovered)
        {
            return false;
        }

        Hovered = row;
        return true;
    }

    /// <summary>
    /// Moves the hover by a number of rows, wraps at the ends and skips rows that cannot be selected.
    /// </summary>
    /// <param name="delta">The step, for example 1 for down and -1 for up.</param>
    /// <returns><see langword="true"/> when <see cref="Hovered"/> changed. It is <see langword="false"/> when no row can be selected.</returns>
    public bool Step(int delta)
    {
        if (Count == 0)
        {
            return false;
        }

        for (var step = 1; step <= Count; step++)
        {
            var row = ((Hovered + (delta * step)) % Count + Count) % Count;
            if (_selectable[row])
            {
                return SetHovered(row);
            }
        }

        return false;
    }

    /// <summary>
    /// Hovers the first row that can be selected, or no row when none can.
    /// </summary>
    /// <returns><see langword="true"/> when <see cref="Hovered"/> changed.</returns>
    public bool SelectFirst()
    {
        for (var row = 0; row < Count; row++)
        {
            if (_selectable[row])
            {
                return SetHovered(row);
            }
        }

        return SetHovered(-1);
    }

    /// <summary>
    /// Tests whether the scale or the hovered row differs from the last <see cref="Rendered"/> call.
    /// </summary>
    /// <param name="scale">The buffer scale the painter will use.</param>
    /// <returns><see langword="true"/> when the menu must be painted again.</returns>
    public bool NeedsRender(int scale) => _rendered != (scale, Hovered);

    /// <summary>
    /// Records that the painter drew the menu at a scale with the current hovered row.
    /// </summary>
    /// <param name="scale">The buffer scale the painter used.</param>
    public void Rendered(int scale) => _rendered = (scale, Hovered);

    /// <summary>
    /// Forgets the last render, so <see cref="NeedsRender"/> returns <see langword="true"/>.
    /// </summary>
    public void Invalidate() => _rendered = null;

    /// <summary>
    /// Moves a menu origin so the menu stays inside an area. A menu larger than the area is placed at zero.
    /// </summary>
    /// <param name="origin">The wanted origin, relative to the area.</param>
    /// <param name="size">The menu size.</param>
    /// <param name="area">The size of the area, for example an output.</param>
    /// <returns>The origin, clamped on each axis.</returns>
    public static Point ClampInto(Point origin, Size size, Size area) => new(
        Math.Clamp(origin.X, 0, Math.Max(area.Width - size.Width, 0)),
        Math.Clamp(origin.Y, 0, Math.Max(area.Height - size.Height, 0)));
}
