namespace Basin.Capabilities;

/// <summary>
/// Records commit and presentation times for each presented surface while at least one holder needs them.
/// </summary>
public interface IFrameStats
{
    /// <summary>
    /// Adds a holder. The first holder starts the recording. Each call must have one matching <see cref="Release"/> call.
    /// </summary>
    void Acquire();

    /// <summary>
    /// Removes a holder. When the last holder releases, the recording stops and all recorded data is discarded.
    /// </summary>
    void Release();

    /// <summary>
    /// Computes the statistics of a surface over the second before a given time.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <param name="nowNanos">The end of the window, in nanoseconds on the monotonic clock.</param>
    /// <param name="snapshot">The statistics, or the default value when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the surface was presented as a main surface while the recording ran, otherwise <see langword="false"/>.</returns>
    bool TryGet(Surface surface, long nowNanos, out FrameStatsSnapshot snapshot);
}
