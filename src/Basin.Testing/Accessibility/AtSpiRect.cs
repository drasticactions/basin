namespace Basin.Testing;

/// <summary>
/// The extents of an accessible object.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct AtSpiRect(int X, int Y, int Width, int Height);
