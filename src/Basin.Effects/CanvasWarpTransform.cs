using Basin.Scene;

namespace Basin.Effects;

public sealed class CanvasWarpTransform : IInvertibleMeshTransform
{
    private static readonly RenderColor White = new(1f, 1f, 1f, 1f);

    private int _cellSize = 16;
    private double _maxFan = double.PositiveInfinity;

    public CanvasWarp? Left { get; set; }

    public CanvasWarp? Right { get; set; }

    public int SceneX { get; set; }

    public int SceneY { get; set; }

    public double MaxFan
    {
        get => _maxFan;
        set => _maxFan = Math.Max(1.0, value);
    }

    public int CellSize
    {
        get => _cellSize;
        set => _cellSize = Math.Max(1, value);
    }

    public double ToScreen(double canvasX)
    {
        if (Left is { } left && left.ContainsCanvas(canvasX))
        {
            return left.ToScreen(canvasX);
        }

        if (Right is { } right && right.ContainsCanvas(canvasX))
        {
            return right.ToScreen(canvasX);
        }

        return canvasX;
    }

    public double ToCanvas(double screenX)
    {
        if (Left is { } left && left.ContainsScreen(screenX))
        {
            return left.ToCanvas(screenX);
        }

        if (Right is { } right && right.ContainsScreen(screenX))
        {
            return right.ToCanvas(screenX);
        }

        return screenX;
    }

    public double FanAt(double canvasX)
    {
        if (Left is { } left && left.ContainsCanvas(canvasX))
        {
            return Math.Min(left.FanAt(canvasX), _maxFan);
        }

        if (Right is { } right && right.ContainsCanvas(canvasX))
        {
            return Math.Min(right.FanAt(canvasX), _maxFan);
        }

        return 1.0;
    }

    public double FanAtScreen(double screenX)
    {
        if (Left is { } left && left.ContainsScreen(screenX))
        {
            return Math.Min(left.FanAtScreen(screenX), _maxFan);
        }

        if (Right is { } right && right.ContainsScreen(screenX))
        {
            return Math.Min(right.FanAtScreen(screenX), _maxFan);
        }

        return 1.0;
    }

    public double ToScreenY(double canvasX, double canvasY)
    {
        if (Left is { } left && left.ContainsCanvas(canvasX))
        {
            return left.Center + ((canvasY - left.Center) * Math.Min(left.FanAt(canvasX), _maxFan));
        }

        if (Right is { } right && right.ContainsCanvas(canvasX))
        {
            return right.Center + ((canvasY - right.Center) * Math.Min(right.FanAt(canvasX), _maxFan));
        }

        return canvasY;
    }

    public double ToCanvasY(double screenX, double screenY)
    {
        if (Left is { } left && left.ContainsScreen(screenX))
        {
            return left.Center + ((screenY - left.Center) / Math.Min(left.FanAtScreen(screenX), _maxFan));
        }

        if (Right is { } right && right.ContainsScreen(screenX))
        {
            return right.Center + ((screenY - right.Center) / Math.Min(right.FanAtScreen(screenX), _maxFan));
        }

        return screenY;
    }

    public bool IsIdentityFor(in Box childBounds)
    {
        var x0 = SceneX + childBounds.X;
        var x1 = SceneX + childBounds.Right;
        if (Left is { } left && (left.ContainsCanvas(x0) || left.ContainsCanvas(x1)))
        {
            return false;
        }

        if (Right is { } right && (right.ContainsCanvas(x0) || right.ContainsCanvas(x1)))
        {
            return false;
        }

        return true;
    }

    public Box MapBounds(in Box childBounds)
    {
        if (childBounds.IsEmpty)
        {
            return childBounds;
        }

        var canvasLeft = SceneX + childBounds.X;
        var canvasRight = SceneX + childBounds.Right;
        var left = ToScreen(canvasLeft) - SceneX;
        var right = ToScreen(canvasRight) - SceneX;
        var x0 = (int)Math.Floor(left);
        var x1 = (int)Math.Ceiling(right);
        var canvasTop = SceneY + childBounds.Y;
        var canvasBottom = SceneY + childBounds.Bottom;
        var top = Math.Min(ToScreenY(canvasLeft, canvasTop), ToScreenY(canvasRight, canvasTop)) - SceneY;
        var bottom = Math.Max(ToScreenY(canvasLeft, canvasBottom), ToScreenY(canvasRight, canvasBottom)) - SceneY;
        var y0 = (int)Math.Floor(top);
        var y1 = (int)Math.Ceiling(bottom);
        return new Box(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    public int VertexCount(in Box childBounds) => childBounds.IsEmpty ? 0 : Columns(childBounds, default) * 6;

    public void WriteVertices(in Box childBounds, Span<MeshVertex> into)
    {
        if (childBounds.IsEmpty)
        {
            return;
        }

        _ = Columns(childBounds, into);
    }

    public bool TryMapToSource(in Box childBounds, double x, double y, out double sourceX, out double sourceY)
    {
        sourceX = ToCanvas(x + SceneX) - SceneX;
        sourceY = ToCanvasY(x + SceneX, y + SceneY) - SceneY;
        return sourceX >= childBounds.X && sourceX < childBounds.Right &&
            sourceY >= childBounds.Y && sourceY < childBounds.Bottom;
    }

    private int Columns(in Box childBounds, Span<MeshVertex> into)
    {
        var count = 0;
        var x0 = SceneX + childBounds.X;
        var x1 = SceneX + childBounds.Right;
        var top = childBounds.Y;
        var bottom = childBounds.Bottom;

        var leftSeam = Left is { IsIdentity: false } left ? left.Seam : int.MinValue;
        var rightSeam = Right is { IsIdentity: false } right ? right.Seam : int.MaxValue;
        if (leftSeam > rightSeam)
        {
            rightSeam = leftSeam;
        }

        var cursor = x0;
        while (cursor < x1 && cursor < leftSeam)
        {
            var next = Math.Min(x1, Math.Min(leftSeam, cursor + _cellSize));
            Emit(cursor, next, top, bottom, ref count, into);
            cursor = next;
        }

        if (cursor < x1 && cursor < rightSeam)
        {
            var next = Math.Min(x1, rightSeam);
            Emit(cursor, next, top, bottom, ref count, into);
            cursor = next;
        }

        while (cursor < x1)
        {
            var next = Math.Min(x1, cursor + _cellSize);
            Emit(cursor, next, top, bottom, ref count, into);
            cursor = next;
        }

        return count;
    }

    private void Emit(int canvasLeft, int canvasRight, int top, int bottom, ref int count, Span<MeshVertex> into)
    {
        if (!into.IsEmpty)
        {
            var screenLeft = (float)(ToScreen(canvasLeft) - SceneX);
            var screenRight = (float)(ToScreen(canvasRight) - SceneX);
            var sourceLeft = canvasLeft - SceneX;
            var sourceRight = canvasRight - SceneX;
            var canvasTop = SceneY + top;
            var canvasBottom = SceneY + bottom;
            var topLeft = (float)(ToScreenY(canvasLeft, canvasTop) - SceneY);
            var topRight = (float)(ToScreenY(canvasRight, canvasTop) - SceneY);
            var bottomLeft = (float)(ToScreenY(canvasLeft, canvasBottom) - SceneY);
            var bottomRight = (float)(ToScreenY(canvasRight, canvasBottom) - SceneY);
            MeshGrid.WriteCell(
                into.Slice(count * 6, 6),
                sourceLeft,
                top,
                sourceRight,
                bottom,
                (screenLeft, topLeft),
                (screenRight, topRight),
                (screenRight, bottomRight),
                (screenLeft, bottomLeft),
                White);
        }

        count++;
    }
}
