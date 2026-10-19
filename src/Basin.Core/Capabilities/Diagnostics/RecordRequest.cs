namespace Basin.Capabilities;

public sealed record RecordRequest(string Path, int Frames)
{
    public string? Output { get; init; }

    public Box Region { get; init; }

    public Func<Box?>? Follow { get; init; }

    public int Margin { get; init; }

    public int MaxDimension { get; init; }

    public int TimeoutMs { get; init; } = 10_000;

    public bool Immediate { get; init; }

    public bool KeepPixels { get; init; }

    public bool InlinePng { get; init; }
}
