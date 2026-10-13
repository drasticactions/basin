namespace Basin.Capabilities;

public readonly record struct FrameStatsSnapshot(
    int Commits,
    int Presents,
    double MeanPresentIntervalMs,
    double P99PresentIntervalMs,
    double MeanLatencyMs,
    double P99LatencyMs,
    IOutput? Output,
    int RefreshMilliHz);
