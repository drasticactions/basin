using Basin.Scene;

namespace Basin.Effects;

public sealed class MeshGrid
{
    private float[] _sourceX = [];
    private float[] _sourceY = [];
    private float[] _pointX = [];
    private float[] _pointY = [];
    private Box _bounds;
    private int _cellSize = 1;

    public Box Bounds => _bounds;

    public int CellSize => _cellSize;

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public int CellCount => Columns * Rows;

    public int PointCount => (Columns + 1) * (Rows + 1);

    public int VertexCount => CellCount * 6;

    public Span<float> PointsX => _pointX.AsSpan(0, PointCount);

    public Span<float> PointsY => _pointY.AsSpan(0, PointCount);

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

    public void Reset()
    {
        var points = PointCount;
        _sourceX.AsSpan(0, points).CopyTo(_pointX);
        _sourceY.AsSpan(0, points).CopyTo(_pointY);
    }

    public int Index(int column, int row) => (row * (Columns + 1)) + column;

    public float SourceX(int column) => _sourceX[Index(column, 0)];

    public float SourceY(int row) => _sourceY[Index(0, row)];

    public void CellSource(int cell, out float left, out float top, out float right, out float bottom)
    {
        var column = cell % Columns;
        var row = cell / Columns;
        left = SourceX(column);
        top = SourceY(row);
        right = SourceX(column + 1);
        bottom = SourceY(row + 1);
    }

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
