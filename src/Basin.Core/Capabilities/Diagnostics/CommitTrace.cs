namespace Basin.Capabilities;

/// <summary>
/// One surface event that <see cref="ISurfaceDiagnostics.WatchCommits"/> reports: a commit or a frame-done.
/// </summary>
/// <param name="Kind">"commit" for a surface commit, or "frame-done" when the compositor sent frame callbacks.</param>
/// <param name="Surface">The surface of the event.</param>
/// <param name="Width">The width of the committed buffer in buffer pixels. It is 0 without a buffer and for frame-done.</param>
/// <param name="Height">The height of the committed buffer in buffer pixels. It is 0 without a buffer and for frame-done.</param>
/// <param name="Format">The format of the committed buffer, or <see cref="DrmFormat.Invalid"/> without a buffer and for frame-done.</param>
/// <param name="Damage">The buffer damage rectangles, then the surface damage rectangles. It is empty for frame-done.</param>
/// <param name="FrameCallbacks">For a commit, the frame callbacks in the committed state. For frame-done, the number of callbacks sent.</param>
/// <param name="Feedback">Whether the commit asked for presentation feedback.</param>
/// <param name="TimestampMs">The frame-done timestamp in milliseconds. It is 0 for a commit.</param>
public readonly record struct CommitTrace(
    string Kind,
    Surface Surface,
    int Width,
    int Height,
    DrmFormat Format,
    IReadOnlyList<Box> Damage,
    int FrameCallbacks,
    bool Feedback,
    uint TimestampMs);
