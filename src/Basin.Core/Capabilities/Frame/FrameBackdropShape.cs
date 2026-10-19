namespace Basin.Capabilities;

public readonly record struct FrameBackdropShape
{
    public double Width { get; init; }

    public double Height { get; init; }

    public double CornerRadius { get; init; }

    public double EdgeWidth { get; init; }

    public Box ClientHole { get; init; }

    public double ClientRadius { get; init; }

    public double HighlightX { get; init; }

    public double HighlightY { get; init; }

    public double HighlightStrength { get; init; }

    public double Emphasis { get; init; }
}
