namespace Basin.Seat;

/// <summary>
/// Maps a layout position into the content of a surface through every transform on the way.
/// </summary>
/// <param name="surface">The surface to map into.</param>
/// <param name="layoutX">The horizontal position in layout coordinates.</param>
/// <param name="layoutY">The vertical position in layout coordinates.</param>
/// <param name="surfaceX">The horizontal surface-local position.</param>
/// <param name="surfaceY">The vertical surface-local position.</param>
/// <returns><see langword="false"/> when the surface is not shown, so that the caller must fall back.</returns>
public delegate bool SurfaceMapper(Surface surface, double layoutX, double layoutY, out double surfaceX, out double surfaceY);
