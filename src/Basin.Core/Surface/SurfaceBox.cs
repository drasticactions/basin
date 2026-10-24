namespace Basin;

/// <summary>A surface and the box that it covers in layout coordinates.</summary>
/// <param name="Surface">The surface.</param>
/// <param name="Box">The box in logical layout coordinates.</param>
public readonly record struct SurfaceBox(Surface Surface, Box Box);
