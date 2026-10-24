namespace Basin.Ipc;

/// <summary>
/// A point in layout coordinates, in logical pixels.
/// </summary>
/// <param name="X">The horizontal coordinate.</param>
/// <param name="Y">The vertical coordinate.</param>
public readonly record struct IpcPoint(int X, int Y);
