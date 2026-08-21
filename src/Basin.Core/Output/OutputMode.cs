using Pixman;

namespace Basin;

public readonly record struct OutputMode(int Width, int Height, int RefreshMilliHz)
{
    public uint RefreshIntervalNanoseconds =>
        RefreshMilliHz > 0 ? (uint)(1_000_000_000_000L / RefreshMilliHz) : 0u;
}
