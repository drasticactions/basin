namespace Basin.WindowManager;

public readonly record struct Size(int Width, int Height)
{
    public static Size Zero => default;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}
