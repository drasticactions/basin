namespace Basin.Scene3D.Scene;

/// <summary>
/// Maps a layout point onto a surface through the world window that shows it.
/// </summary>
public static class WorldSurfaceMapper
{
    /// <summary>
    /// Maps a layout point onto <paramref name="surface"/> through the plane of the first window layer that shows it. It suits <c>SeatPointer.MapToSurface</c>.
    /// </summary>
    /// <param name="world">The world that holds the windows.</param>
    /// <param name="camera">The camera that spans the layout.</param>
    /// <param name="layout">The output layout.</param>
    /// <param name="surface">The surface to map onto.</param>
    /// <param name="x">The x coordinate in layout pixels.</param>
    /// <param name="y">The y coordinate in layout pixels.</param>
    /// <param name="surfaceX">The x coordinate in the surface's logical pixels. It can be outside the surface.</param>
    /// <param name="surfaceY">The y coordinate in the surface's logical pixels.</param>
    /// <returns><see langword="false"/> when no window shows the surface or the ray cannot reach the layer's plane.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/>, <paramref name="camera"/> or <paramref name="layout"/> is <see langword="null"/>.</exception>
    public static bool TryMap(
        World world, WorldCamera camera, OutputLayout layout, Surface surface, double x, double y, out double surfaceX, out double surfaceY)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(layout);
        surfaceX = 0;
        surfaceY = 0;
        var windows = world.Windows;
        for (var w = 0; w < windows.Count; w++)
        {
            var layers = windows[w].Layers;
            for (var i = 0; i < layers.Length; i++)
            {
                if (ReferenceEquals(layers[i].Surface, surface))
                {
                    return camera.TryRayAt(layout, x, y, out var ray)
                        && WorldPicker.TryOnPlane(windows[w], i, ray, out surfaceX, out surfaceY);
                }
            }
        }

        return false;
    }
}
