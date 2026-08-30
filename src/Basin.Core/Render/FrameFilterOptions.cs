namespace Basin;

public readonly record struct FrameFilterOptions
{
    public ulong FrameCount { get; init; }

    public float FramesPerSecond { get; init; }

    public uint FrametimeDeltaMillis { get; init; }

    public uint Rotation { get; init; }
}
