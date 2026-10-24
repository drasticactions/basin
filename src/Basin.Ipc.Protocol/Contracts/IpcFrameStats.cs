namespace Basin.Ipc;

/// <summary>
/// The reply of <c>windows/stats</c> and the data of a <c>frame/stats</c> event.
/// It covers the last second of one window. Every value is 0 when the session has no frame statistics.
/// </summary>
/// <param name="Id">The window id.</param>
/// <param name="CommitsPerSecond">The count of surface commits in the last second.</param>
/// <param name="PresentsPerSecond">The count of commits that reached the screen in the last second.</param>
/// <param name="PresentIntervalMeanMs">The mean time between two presents, in milliseconds, rounded to three decimals.</param>
/// <param name="PresentIntervalP99Ms">The 99th percentile of the time between two presents, in milliseconds.</param>
/// <param name="LatencyMeanMs">The mean time from a commit to the present that showed it, in milliseconds.</param>
/// <param name="LatencyP99Ms">The 99th percentile of the commit-to-present latency, in milliseconds.</param>
/// <param name="Output">The name of the output that presents the window, or <see langword="null"/> when no output presented it.</param>
/// <param name="RefreshMhz">The refresh rate of that output in millihertz, or 0 with no output.</param>
public readonly record struct IpcFrameStats(
    ulong Id,
    int CommitsPerSecond,
    int PresentsPerSecond,
    double PresentIntervalMeanMs,
    double PresentIntervalP99Ms,
    double LatencyMeanMs,
    double LatencyP99Ms,
    string? Output,
    int RefreshMhz);
