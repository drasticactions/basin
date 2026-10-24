using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Divides a rectangle into a grid of cells of at most a given side, aligned to its origin, with the last row and column clipped. It keeps a movable point per grid corner for a mesh deformation.
/// </summary>
public sealed class MeshGrid
{
    private float[] _sourceX = [];
    private float[] _sourceY = [];
    private float[] _pointX = [];
    private float[] _pointY = [];
    private Box _bounds;
    private int _cellSize = 1;

    /// <summary>
    /// Gets the rectangle of the last <see cref="Layout"/>.
    /// </summary>
    public Box Bounds => _bounds;

    /// <summary>
    /// Gets the largest cell side of the last <see cref="Layout"/>, in pixels.
    /// </summary>
    public int CellSize => _cellSize;

    /// <summary>
    /// Gets the number of cell columns.
    /// </summary>
    public int Columns { get; private set; }

    /// <summary>
    /// Gets the number of cell rows.
    /// </summary>
    public int Rows { get; private set; }

    /// <summary>
    /// Gets the number of cells, <see cref="Columns"/> times <see cref="Rows"/>.
    /// </summary>
    public int CellCount => Columns * Rows;

    /// <summary>
    /// Gets the number of grid corners, one more than the columns times one more than the rows.
    /// </summary>
    public int PointCount => (Columns + 1) * (Rows + 1);

    /// <summary>
    /// Gets the number of vertices that <see cref="Write"/> writes, six per cell.
    /// </summary>
    public int VertexCount => CellCount * 6;

    /// <summary>
    /// Gets the horizontal positions of the movable points, in the order of <see cref="Index"/>. A caller writes the deformation here.
    /// </summary>
    public Span<float> PointsX => _pointX.AsSpan(0, PointCount);

    /// <summary>
    /// Gets the vertical positions of the movable points, in the order of <see cref="Index"/>. A caller writes the deformation here.
    /// </summary>
    public Span<float> PointsY => _pointY.AsSpan(0, PointCount);

    /// <summary>
    /// Lays out the grid for a rectangle and resets the points to their source positions. It does nothing when the rectangle and the cell size did not change.
    /// </summary>
    /// <param name="bounds">The rectangle to divide.</param>
    /// <param name="maxCellSize">The largest cell side, in pixels. Values below 1 use 1.</param>
    /// <returns><see langword="true"/> when the grid changed and the points were reset.</returns>
    public bool Layout(in Box bounds, int maxCellSize)
    {
        var cell = Math.Max(1, maxCellSize);
        if (_bounds == bounds && _cellSize == cell && Columns > 0)
        {
            return false;
        }

        _bounds = bounds;
        _cellSize = cell;
        Columns = bounds.Width <= 0 ? 0 : (bounds.Width + cell - 1) / cell;
        Rows = bounds.Height <= 0 ? 0 : (bounds.Height + cell - 1) / cell;
        var points = PointCount;
        if (_sourceX.Length < points)
        {
            _sourceX = new float[points];
            _sourceY = new float[points];
            _pointX = new float[points];
            _pointY = new float[points];
        }

        for (var j = 0; j <= Rows; j++)
        {
            var y = Math.Min(bounds.Y + ((long)j * cell), bounds.Bottom);
            for (var i = 0; i <= Columns; i++)
            {
                var x = Math.Min(bounds.X + ((long)i * cell), bounds.Right);
                _sourceX[(j * (Columns + 1)) + i] = x;
                _sourceY[(j * (Columns + 1)) + i] = y;
            }
        }

        Reset();
        return true;
    }

    /// <summary>
    /// Moves every point back to its source position.
    /// </summary>
    public void Reset()
    {
        var points = PointCount;
        _sourceX.AsSpan(0, points).CopyTo(_pointX);
        _sourceY.AsSpan(0, points).CopyTo(_pointY);
    }

    /// <summary>
    /// Calculates the index of a grid corner in <see cref="PointsX"/> and <see cref="PointsY"/>.
    /// </summary>
    /// <param name="column">The corner column, from 0 to <see cref="Columns"/>.</param>
    /// <param name="row">The corner row, from 0 to <see cref="Rows"/>.</param>
    /// <returns>The point index.</returns>
    public int Index(int column, int row) => (row * (Columns + 1)) + column;

    /// <summary>
    /// Gets the undeformed horizontal position of a corner column.
    /// </summary>
    /// <param name="column">The corner column, from 0 to <see cref="Columns"/>.</param>
    /// <returns>The position, clipped to the right edge of <see cref="Bounds"/>.</returns>
    public float SourceX(int column) => _sourceX[Index(column, 0)];

    /// <summary>
    /// Gets the undeformed vertical position of a corner row.
    /// </summary>
    /// <param name="row">The corner row, from 0 to <see cref="Rows"/>.</param>
    /// <returns>The position, clipped to the bottom edge of <see cref="Bounds"/>.</returns>
    public float SourceY(int row) => _sourceY[Index(0, row)];

    /// <summary>
    /// Gets the undeformed edges of a cell. Cells are in rows from the top left.
    /// </summary>
    /// <param name="cell">The cell index, below <see cref="CellCount"/>.</param>
    /// <param name="left">The left edge.</param>
    /// <param name="top">The top edge.</param>
    /// <param name="right">The right edge.</param>
    /// <param name="bottom">The bottom edge.</param>
    public void CellSource(int cell, out float left, out float top, out float right, out float bottom)
    {
        var column = cell % Columns;
        var row = cell / Columns;
        left = SourceX(column);
        top = SourceY(row);
        right = SourceX(column + 1);
        bottom = SourceY(row + 1);
    }

    /// <summary>
    /// Writes two white triangles per cell from the shared points, so adjacent cells keep their corners together. The texture coordinates are the source positions.
    /// </summary>
    /// <param name="into">The destination, with at least <see cref="VertexCount"/> elements.</param>
    public void Write(Span<MeshVertex> into)
    {
        var white = new RenderColor(1f, 1f, 1f, 1f);
        var write = 0;
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var topLeft = Index(column, row);
                var topRight = topLeft + 1;
                var bottomLeft = Index(column, row + 1);
                var bottomRight = bottomLeft + 1;
                Span<int> corners = [topLeft, topRight, bottomLeft, topRight, bottomRight, bottomLeft];
                foreach (var corner in corners)
                {
                    into[write++] = new MeshVertex(
                        _pointX[corner], _pointY[corner], _sourceX[corner], _sourceY[corner], white);
                }
            }
        }
    }

    /// <summary>
    /// Writes the two triangles of one cell from four independent corners, for an effect whose cells come apart.
    /// </summary>
    /// <param name="into">The destination, with at least six elements.</param>
    /// <param name="left">The left texture coordinate.</param>
    /// <param name="top">The top texture coordinate.</param>
    /// <param name="right">The right texture coordinate.</param>
    /// <param name="bottom">The bottom texture coordinate.</param>
    /// <param name="topLeft">The position of the top-left corner.</param>
    /// <param name="topRight">The position of the top-right corner.</param>
    /// <param name="bottomRight">The position of the bottom-right corner.</param>
    /// <param name="bottomLeft">The position of the bottom-left corner.</param>
    /// <param name="color">The vertex color.</param>
    public static void WriteCell(
        Span<MeshVertex> into,
        float left,
        float top,
        float right,
        float bottom,
        (float X, float Y) topLeft,
        (float X, float Y) topRight,
        (float X, float Y) bottomRight,
        (float X, float Y) bottomLeft,
        in RenderColor color)
    {
        into[0] = new MeshVertex(topLeft.X, topLeft.Y, left, top, color);
        into[1] = new MeshVertex(topRight.X, topRight.Y, right, top, color);
        into[2] = new MeshVertex(bottomLeft.X, bottomLeft.Y, left, bottom, color);
        into[3] = new MeshVertex(topRight.X, topRight.Y, right, top, color);
        into[4] = new MeshVertex(bottomRight.X, bottomRight.Y, right, bottom, color);
        into[5] = new MeshVertex(bottomLeft.X, bottomLeft.Y, left, bottom, color);
    }
}
