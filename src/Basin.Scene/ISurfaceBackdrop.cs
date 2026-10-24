using Pixman;

namespace Basin.Scene;

/// <summary>
/// Supplies the backdrop effect, for example a blur, that surfaces in a <see cref="Scene"/> request behind a region of themselves.
/// </summary>
public interface ISurfaceBackdrop
{
    /// <summary>
    /// Gets the effect to draw behind the requested regions. It is <see langword="null"/> when no effect is available.
    /// </summary>
    IBackdropEffect? Effect { get; }

    /// <summary>
    /// Writes the region that a surface wants the backdrop effect behind.
    /// </summary>
    /// <param name="surface">The surface to query.</param>
    /// <param name="into">The region to fill, in surface-local logical pixels.</param>
    /// <returns><see langword="false"/> when the surface requests no backdrop.</returns>
    bool RegionOf(Surface surface, PixmanRegion32 into);

    /// <summary>
    /// Drops the state that the effect keeps for a scene node that goes away or changes its backdrop owner.
    /// </summary>
    /// <param name="key">The node that the effect keyed its state on.</param>
    void Forget(object key);
}
