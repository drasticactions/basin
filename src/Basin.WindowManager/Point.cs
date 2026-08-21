namespace Basin.WindowManager;

public readonly record struct Point(int X, int Y)
{
    public static Point Zero => default;
}
