namespace Basin;

public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(Point p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;

    public bool Contains(in Box other) =>
        other.IsEmpty || (other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom);

    public Box Translated(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public Box Intersect(in Box other)
    {
        var x1 = Math.Max(X, other.X);
        var y1 = Math.Max(Y, other.Y);
        var x2 = Math.Min(Right, other.Right);
        var y2 = Math.Min(Bottom, other.Bottom);
        return new Box(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }
}
