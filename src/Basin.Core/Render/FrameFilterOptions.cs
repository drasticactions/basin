namespace Basin;

/// <summary>
/// Carries the per-frame values that a frame filter can read, in the shape that librashader expects.
/// </summary>
public readonly record struct FrameFilterOptions
{
    /// <summary>
    /// Gets a counter that grows by one for each filtered frame. The scene starts it at 1.
    /// </summary>
    public ulong FrameCount { get; init; }

    /// <summary>
    /// Gets the refresh rate of the output, or zero when it is unknown.
    /// </summary>
    public float FramesPerSecond { get; init; }

    /// <summary>
    /// Gets the time in milliseconds since the previous filtered frame, up to 1000. It is zero for the first frame.
    /// </summary>
    public uint FrametimeDeltaMillis { get; init; }

    /// <summary>
    /// Gets the output rotation in quarter turns, from 0 to 3.
    /// </summary>
    public uint Rotation { get; init; }
}
