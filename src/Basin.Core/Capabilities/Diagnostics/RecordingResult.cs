namespace Basin.Capabilities;

public sealed record RecordingResult(
    string Path,
    int Frames,
    int Width,
    int Height,
    int ImageWidth,
    int ImageHeight,
    double LumaLow,
    double LumaHigh,
    double PeakLow,
    double PeakHigh,
    int DeltaMax,
    bool TimedOut,
    IReadOnlyList<RecordFrameStats> PerFrame)
{
    public byte[]? Pixels { get; init; }

    public byte[]? Png { get; init; }
}
