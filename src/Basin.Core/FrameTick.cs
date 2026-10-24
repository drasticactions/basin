namespace Basin;

/// <summary>
/// Describes the frame that an output is about to render, in nanoseconds. Animations step on it.
/// </summary>
/// <param name="TargetPresentNanos">The predicted present time of the frame as animation time. It is the monotonic time unless an <see cref="Capabilities.IAnimationClock"/> converts it. It is 0 when the backend gives no prediction.</param>
/// <param name="RefreshIntervalNanos">The refresh period of the output mode.</param>
public readonly record struct FrameTick(long TargetPresentNanos, long RefreshIntervalNanos);
