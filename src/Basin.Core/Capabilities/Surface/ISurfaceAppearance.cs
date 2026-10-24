using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// Holds the opacity and the visible region that protocols set for a surface. The scene reads them when it draws the surface.
/// </summary>
public interface ISurfaceAppearance
{
    /// <summary>
    /// Gets the opacity of a surface.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The opacity from 0 to 1. It is 1 for a surface that has no setting.</returns>
    double OpacityOf(Surface surface);

    /// <summary>
    /// Gets the region of a surface that the compositor draws. The region stays owned by the implementation and the caller must not keep or dispose it.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <param name="region">The visible region in surface-local coordinates, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the surface has a non-empty visible region, otherwise <see langword="false"/>.</returns>
    bool TryVisibleRegion(Surface surface, out PixmanRegion32 region);

    /// <summary>
    /// Sets the opacity of a surface and notifies the observers when the value changes.
    /// </summary>
    /// <param name="surface">The surface to change.</param>
    /// <param name="opacity">The opacity. A value outside 0 to 1 is clamped.</param>
    void SetOpacity(Surface surface, double opacity);

    /// <summary>
    /// Sets the region of a surface that the compositor draws and notifies the observers when it changes. An empty region clears the setting.
    /// </summary>
    /// <param name="surface">The surface to change.</param>
    /// <param name="region">The visible region in surface-local coordinates. The implementation copies it, so the caller keeps ownership.</param>
    void SetVisibleRegion(Surface surface, PixmanRegion32 region);

    /// <summary>
    /// Removes the visible region of a surface, so that the whole surface draws again.
    /// </summary>
    /// <param name="surface">The surface to change.</param>
    void ClearVisibleRegion(Surface surface);

    /// <summary>
    /// Adds an observer that is told when the opacity or the visible region of any surface changes.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddObserver(ISurfaceAppearanceObserver observer);

    /// <summary>
    /// Removes an observer. It is safe to call from inside a notification.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveObserver(ISurfaceAppearanceObserver observer);
}
