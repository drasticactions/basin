namespace Basin.Frames.Metacity;

internal record struct MetacityBorder(int Top, int Bottom, int Left, int Right)
{
    public static MetacityBorder Unset => new(-1, -1, -1, -1);

    public string? UnsetSide =>
        Top < 0 ? "top" : Bottom < 0 ? "bottom" : Left < 0 ? "left" : Right < 0 ? "right" : null;
}
