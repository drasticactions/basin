namespace Basin.Ipc;

/// <summary>
/// The reply of the <c>diag/clock</c> methods: the state of the animation clock after the call.
/// </summary>
/// <param name="Frozen">A value indicating whether the clock is stopped.</param>
/// <param name="Nanos">The animation time in nanoseconds, on the time base of the monotonic clock.</param>
/// <param name="Animating">A value indicating whether an animation asked for a frame after the last frame began.</param>
public readonly record struct IpcDiagClock(bool Frozen, long Nanos, bool Animating);
