namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/record</c> and <c>diag/record-window</c>. The frames are stacked top to bottom in one PNG strip.
/// </summary>
/// <param name="Path">The PNG path of the request. It is empty when the PNG comes inline. The file exists only when a frame was recorded.</param>
/// <param name="Frames">The number of recorded frames.</param>
/// <param name="Width">The width of one frame in physical pixels.</param>
/// <param name="Height">The height of one frame in physical pixels.</param>
/// <param name="ImageWidth">The width of the strip after any downscale. It is 0 when no frame was recorded.</param>
/// <param name="ImageHeight">The height of the strip after any downscale. It is 0 when no frame was recorded.</param>
/// <param name="LumaLow">The lowest mean luma of a frame.</param>
/// <param name="LumaHigh">The highest mean luma of a frame.</param>
/// <param name="PeakLow">The lowest peak luma of a frame.</param>
/// <param name="PeakHigh">The highest peak luma of a frame.</param>
/// <param name="DeltaMax">The largest number of changed pixels between two frames.</param>
/// <param name="TimedOut">Whether the recording ended before it had all its frames.</param>
/// <param name="PerFrame">The statistics of each frame.</param>
/// <param name="Png">The PNG strip, written as base64, when the request named no path. Otherwise <see langword="null"/>.</param>
public readonly record struct IpcDiagRecording(
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
    IpcDiagRecordFrame[] PerFrame,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] byte[]? Png = null);
