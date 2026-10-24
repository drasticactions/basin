namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/drag</c>. All points are in layout coordinates.
/// </summary>
/// <param name="Window">The id of the dragged window.</param>
/// <param name="Part">The name of the pressed frame part.</param>
/// <param name="FromX">The x of the press, the center of the part.</param>
/// <param name="FromY">The y of the press, the center of the part.</param>
/// <param name="ToX">The x of the release.</param>
/// <param name="ToY">The y of the release.</param>
/// <param name="Steps">The number of pointer moves between the press and the release.</param>
/// <param name="Settled">Whether a frame started and the window made no commit for 100 ms before the timeout.</param>
/// <param name="Frames">The number of frames that the frame clock began while the drag ran.</param>
public readonly record struct IpcDiagDrag(
    ulong Window, string Part, double FromX, double FromY, double ToX, double ToY, int Steps, bool Settled, int Frames);
