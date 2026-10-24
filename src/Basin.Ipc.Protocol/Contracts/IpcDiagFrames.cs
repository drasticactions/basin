namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/frames</c>.
/// </summary>
/// <param name="Frames">One entry for each window frame.</param>
/// <param name="ClockSinks">The number of sinks on the frame clock. It is -1 when the clock is not the default one or there is no scene yet.</param>
public readonly record struct IpcDiagFrames(IpcDiagFrame[] Frames, int ClockSinks);
