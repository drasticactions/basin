namespace Basin.Capabilities;

/// <summary>
/// Holds frame statistics for one surface over the last second before the snapshot time.
/// </summary>
/// <param name="Commits">The number of commits in the window.</param>
/// <param name="Presents">The number of presentations that showed a new commit in the window.</param>
/// <param name="MeanPresentIntervalMs">The mean time between those presentations, in milliseconds. It is 0 with fewer than two presentations.</param>
/// <param name="P99PresentIntervalMs">The 99th percentile of the time between presentations, in milliseconds.</param>
/// <param name="MeanLatencyMs">The mean time from the last commit to its presentation, in milliseconds.</param>
/// <param name="P99LatencyMs">The 99th percentile of the commit-to-presentation time, in milliseconds.</param>
/// <param name="Output">The output that last presented the surface, or <see langword="null"/> before the first presentation.</param>
/// <param name="RefreshMilliHz">The refresh rate of the current mode of <paramref name="Output"/> in millihertz, or 0 when there is no output.</param>
public readonly record struct FrameStatsSnapshot(
    int Commits,
    int Presents,
    double MeanPresentIntervalMs,
    double P99PresentIntervalMs,
    double MeanLatencyMs,
    double P99LatencyMs,
    IOutput? Output,
    int RefreshMilliHz);
