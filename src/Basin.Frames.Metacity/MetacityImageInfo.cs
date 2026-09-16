namespace Basin.Frames.Metacity;

internal sealed class MetacityImageInfo
{
    public required string Filename { get; init; }

    public required string Path { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public bool HorizontalStripes { get; init; }

    public bool VerticalStripes { get; init; }

    public bool IsSvg { get; init; }
}
