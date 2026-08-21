namespace Basin;

public readonly record struct FBox(double X, double Y, double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool IsPixelAligned =>
        X == Math.Floor(X) && Y == Math.Floor(Y) && Width == Math.Floor(Width) && Height == Math.Floor(Height);

    public Box RoundedOut()
    {
        var x1 = (int)Math.Floor(X);
        var y1 = (int)Math.Floor(Y);
        var x2 = (int)Math.Ceiling(Right);
        var y2 = (int)Math.Ceiling(Bottom);
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }

    public static implicit operator FBox(Box box) => new(box.X, box.Y, box.Width, box.Height);
}
