namespace Basin.Capabilities;

/// <summary>
/// Receives changes from an <see cref="ISurfaceAppearance"/>.
/// </summary>
public interface ISurfaceAppearanceObserver
{
    /// <summary>
    /// Called after the opacity or the visible region of a surface changes.
    /// </summary>
    /// <param name="surface">The surface that changed.</param>
    void AppearanceChanged(Surface surface);
}
