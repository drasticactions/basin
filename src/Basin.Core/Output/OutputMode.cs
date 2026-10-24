using Pixman;

namespace Basin;

/// <summary>
/// Describes a display mode in physical pixels.
/// </summary>
/// <param name="Width">The width in physical pixels.</param>
/// <param name="Height">The height in physical pixels.</param>
/// <param name="RefreshMilliHz">The refresh rate in millihertz, or zero when the mode has no fixed refresh.</param>
public readonly record struct OutputMode(int Width, int Height, int RefreshMilliHz)
{
    /// <summary>
    /// Gets the length of one refresh cycle in nanoseconds, or zero when <see cref="RefreshMilliHz"/> is zero.
    /// </summary>
    public uint RefreshIntervalNanoseconds =>
        RefreshMilliHz > 0 ? (uint)(1_000_000_000_000L / RefreshMilliHz) : 0u;
}
