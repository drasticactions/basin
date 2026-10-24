namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/parts</c>.
/// </summary>
/// <param name="Window">The window id. It is 0 when the parts come from a parts source with no window model.</param>
/// <param name="AppId">The app id of the window.</param>
/// <param name="Title">The title of the window.</param>
/// <param name="Parts">Each part of the frame.</param>
public readonly record struct IpcDiagParts(ulong Window, string AppId, string Title, IpcDiagPart[] Parts);
