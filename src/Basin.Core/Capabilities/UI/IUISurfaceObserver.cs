using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// Receives the damage and the destruction of an <see cref="IUISurface"/>.
/// Each call names the surface, so one observer can watch several surfaces.
/// </summary>
public interface IUISurfaceObserver
{
    /// <summary>
    /// The surface calls this method after it has new content. Call <see cref="IUISurface.TryAcquire"/> to show it.
    /// </summary>
    /// <param name="surface">The surface that changed.</param>
    /// <param name="damage">The changed region. An empty region means the whole surface. It is valid only during the call.</param>
    void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage);

    /// <summary>
    /// The surface calls this method when it is disposed. The observer must drop its reference to the surface.
    /// </summary>
    /// <param name="surface">The disposed surface.</param>
    void OnSurfaceDestroyed(IUISurface surface);
}
