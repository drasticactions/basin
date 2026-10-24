namespace Basin.Capabilities;

/// <summary>
/// Describes a recording for <see cref="ISceneDiagnostics.Record"/>.
/// </summary>
/// <param name="Path">The file for the PNG strip, or an empty string for no file.</param>
/// <param name="Frames">The number of frames to record. It must be at least 1, and the recorder records 600 at most.</param>
public sealed record RecordRequest(string Path, int Frames)
{
    /// <summary>
    /// Gets the name of the output to record. When it is <see langword="null"/>, the recorder uses the output under the followed target, or else the first output.
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Gets the box to record in output physical pixels. The recorder uses it only when <see cref="Follow"/> is <see langword="null"/>.
    /// </summary>
    public Box Region { get; init; }

    /// <summary>
    /// Gets a locator for a target in scene coordinates. Its first box sets the recorded size, and each frame centers that size on the target.
    /// </summary>
    public Func<Box?>? Follow { get; init; }

    /// <summary>
    /// Gets the margin in logical pixels that the caller gave to the locator. The recorder does not read it.
    /// </summary>
    public int Margin { get; init; }

    /// <summary>
    /// Gets the largest width or height of one frame in the PNG. A larger frame is downscaled to fit. 0 means no limit.
    /// </summary>
    public int MaxDimension { get; init; }

    /// <summary>
    /// Gets the time in milliseconds after which the recording ends with the frames that it has. The default is 10000.
    /// </summary>
    public int TimeoutMs { get; init; } = 10_000;

    /// <summary>
    /// Gets a value indicating whether the frame on screen now becomes the first frame, instead of the next presented frame.
    /// </summary>
    public bool Immediate { get; init; }

    /// <summary>
    /// Gets a value indicating whether the result carries the raw RGBA strip in <see cref="RecordingResult.Pixels"/>.
    /// </summary>
    public bool KeepPixels { get; init; }

    /// <summary>
    /// Gets a value indicating whether the result carries the encoded PNG in <see cref="RecordingResult.Png"/>.
    /// </summary>
    public bool InlinePng { get; init; }
}
