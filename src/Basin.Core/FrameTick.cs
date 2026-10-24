namespace Basin;

/// <summary>
/// Describes the frame that an output is about to render, in nanoseconds of the monotonic clock.
/// </summary>
/// <param name="TargetPresentNanos">The predicted present time of the frame. It is 0 when the backend gives no prediction.</param>
/// <param name="RefreshIntervalNanos">The refresh period of the output mode.</param>
public readonly record struct FrameTick(long TargetPresentNanos, long RefreshIntervalNanos);
