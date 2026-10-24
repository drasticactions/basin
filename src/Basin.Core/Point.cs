namespace Basin;

/// <summary>
/// An integer point in a coordinate space that the caller defines.
/// </summary>
/// <param name="X">The horizontal coordinate.</param>
/// <param name="Y">The vertical coordinate.</param>
public readonly record struct Point(int X, int Y);
