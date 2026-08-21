namespace Basin.WindowManager;

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public static Rect Empty => default;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public Point Position => new(X, Y);

    public Size Size => new(Width, Height);

    public bool Contains(Point point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public Rect Intersect(Rect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= x || bottom <= y ? Empty : new Rect(x, y, right - x, bottom - y);
    }
}
