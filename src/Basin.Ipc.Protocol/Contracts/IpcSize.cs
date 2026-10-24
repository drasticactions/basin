namespace Basin.Ipc;

/// <summary>
/// A width and a height. The unit comes from the field that holds the size.
/// </summary>
/// <param name="Width">The horizontal extent.</param>
/// <param name="Height">The vertical extent.</param>
public readonly record struct IpcSize(int Width, int Height);
