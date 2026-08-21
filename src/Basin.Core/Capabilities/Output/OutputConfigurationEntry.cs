namespace Basin.Capabilities;

public readonly record struct OutputConfigurationEntry
{
    public required IOutput Output { get; init; }

    public bool Enabled { get; init; }

    public OutputMode? Mode { get; init; }

    public Point? Position { get; init; }

    public double? Scale { get; init; }

    public OutputTransform? Transform { get; init; }

    public bool? AdaptiveSync { get; init; }
}
