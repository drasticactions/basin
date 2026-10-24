namespace Basin.Ipc;

/// <summary>
/// One part of a window frame.
/// </summary>
/// <param name="Part">The part name, for example <c>title</c>, <c>close</c> or <c>top-left</c>.</param>
/// <param name="Box">The bounds of the part in layout coordinates.</param>
public readonly record struct IpcDiagPart(string Part, IpcBox Box);
