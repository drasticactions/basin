namespace Basin.Effects;

/// <summary>
/// Reports the rectangle that the full-screen zoom follows when <see cref="ZoomOptions.FocusTracking"/> is set. The consumer chooses the focus, the text caret, or either.
/// </summary>
public interface IZoomTarget
{
    /// <summary>
    /// Gets the rectangle to follow and the time when it was reported.
    /// </summary>
    /// <param name="rectangle">The rectangle, in output pixels.</param>
    /// <param name="reportedAtNanos">The time of the report, in nanoseconds on the clock of the cursor times that <see cref="ZoomStage.SetCursor"/> receives.</param>
    /// <returns><see langword="true"/> when there is a rectangle to follow.</returns>
    bool TryGetFocus(out Box rectangle, out long reportedAtNanos);
}
