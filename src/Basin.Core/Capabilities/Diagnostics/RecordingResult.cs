namespace Basin.Capabilities;

/// <summary>
/// The result of a recording that <see cref="ISceneDiagnostics.Record"/> started. The frames are stacked top to bottom in one strip image.
/// </summary>
/// <param name="Path">The PNG path of the request. The file exists only when the path is not empty and a frame was recorded.</param>
/// <param name="Frames">The number of recorded frames.</param>
/// <param name="Width">The width of one frame in physical pixels.</param>
/// <param name="Height">The height of one frame in physical pixels.</param>
/// <param name="ImageWidth">The width of the strip image after any downscale. It is 0 when no frame was recorded.</param>
/// <param name="ImageHeight">The height of the strip image after any downscale. It is 0 when no frame was recorded.</param>
/// <param name="LumaLow">The lowest mean luma of a frame.</param>
/// <param name="LumaHigh">The highest mean luma of a frame.</param>
/// <param name="PeakLow">The lowest peak luma of a frame.</param>
/// <param name="PeakHigh">The highest peak luma of a frame.</param>
/// <param name="DeltaMax">The largest number of changed pixels between two frames.</param>
/// <param name="TimedOut">Whether the recording ended before it had all frames. The recorder records 600 frames at most.</param>
/// <param name="PerFrame">The statistics of each frame.</param>
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
    /// <summary>
    /// Gets the RGBA strip of all frames at full size. It is <see langword="null"/> unless the request set <see cref="RecordRequest.KeepPixels"/> and a frame was recorded.
    /// </summary>
    public byte[]? Pixels { get; init; }

    /// <summary>
    /// Gets the encoded PNG strip. It is <see langword="null"/> unless the request set <see cref="RecordRequest.InlinePng"/> and a frame was recorded.
    /// </summary>
    public byte[]? Png { get; init; }
}
