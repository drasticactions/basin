using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/compare</c>. When it timed out, every count and size of the shown frame is 0.
/// </summary>
/// <param name="Window">The id of the window of the target, or 0 when the target is not a window.</param>
/// <param name="Passed">Whether the sizes match and at most <paramref name="Pixels"/> pixels changed.</param>
/// <param name="SameSize">Whether the shown frame and the reference have the same size.</param>
/// <param name="Width">The width of the shown frame in pixels.</param>
/// <param name="Height">The height of the shown frame in pixels.</param>
/// <param name="ReferenceWidth">The width of the reference PNG.</param>
/// <param name="ReferenceHeight">The height of the reference PNG.</param>
/// <param name="Changed">The number of pixels with a channel that differs by more than <paramref name="Channel"/>.</param>
/// <param name="MaxDelta">The largest difference of one channel.</param>
/// <param name="ChangedBox">The bounding box of the changed pixels, in image pixels.</param>
/// <param name="Channel">The channel difference that the compare tolerated.</param>
/// <param name="Pixels">The number of changed pixels that the compare tolerated.</param>
/// <param name="DiffPath">The path of the written diff PNG, or <see langword="null"/> when none was asked for or written.</param>
/// <param name="TimedOut">Whether no frame was read before the timeout.</param>
/// <param name="Png">The diff PNG, written as base64, or <see langword="null"/> unless <c>inline</c> was asked for.</param>
public readonly record struct IpcDiagComparison(
    ulong Window,
    bool Passed,
    bool SameSize,
    int Width,
    int Height,
    int ReferenceWidth,
    int ReferenceHeight,
    int Changed,
    int MaxDelta,
    IpcBox ChangedBox,
    int Channel,
    int Pixels,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DiffPath,
    bool TimedOut,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] byte[]? Png = null);
