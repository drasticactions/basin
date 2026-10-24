namespace Basin.Avalonia;

internal sealed class BasinEglDisplayMessage(nint display)
{
    public nint Display { get; } = display;
}
