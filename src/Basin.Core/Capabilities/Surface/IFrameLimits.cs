namespace Basin.Capabilities;

/// <summary>
/// Holds a frame-rate cap for each surface. The compositor paces frame callbacks of a capped surface to the cap.
/// </summary>
public interface IFrameLimits
{
    /// <summary>
    /// Gets the frame-rate cap of a surface.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The cap in frames per second, or 0 when the surface has no cap.</returns>
    int LimitOf(Surface surface);

    /// <summary>
    /// Sets or removes the frame-rate cap of a surface. A cap ends when the surface is destroyed.
    /// </summary>
    /// <param name="surface">The surface to cap.</param>
    /// <param name="fps">The cap in frames per second, or 0 to remove the cap. It must not be negative.</param>
    void SetLimit(Surface surface, int fps);

    /// <summary>
    /// Occurs when the cap of a surface changes, with the surface as the argument. Setting the same cap again does not raise it.
    /// </summary>
    event Action<Surface>? Changed;
}
