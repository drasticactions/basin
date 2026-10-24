namespace Basin.Ipc;

/// <summary>
/// A box on the wire as <c>x</c>, <c>y</c>, <c>width</c> and <c>height</c>. The coordinate space is the one that the field that holds it names.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct IpcBox(int X, int Y, int Width, int Height);
