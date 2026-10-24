namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/click</c>.
/// </summary>
/// <param name="Window">The id of the clicked window.</param>
/// <param name="Part">The name of the clicked frame part, for example <c>close</c>.</param>
/// <param name="X">The x of the click in layout coordinates.</param>
/// <param name="Y">The y of the click in layout coordinates.</param>
/// <param name="Settled">Whether a frame started and the window made no commit for 100 ms before the timeout.</param>
public readonly record struct IpcDiagClick(ulong Window, string Part, double X, double Y, bool Settled);
