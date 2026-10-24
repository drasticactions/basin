namespace Basin.Scene;

/// <summary>
/// The result of a screenshot of a presented frame.
/// </summary>
public enum ScreenshotOutcome
{
    /// <summary>
    /// The PNG file was written.
    /// </summary>
    Written,

    /// <summary>
    /// No frame was presented, or its buffer is destroyed. No file was written.
    /// </summary>
    NoFrame,

    /// <summary>
    /// The renderer did not import or read the buffer. No file was written.
    /// </summary>
    Unreadable,
}
