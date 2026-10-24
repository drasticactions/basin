using Basin.Scene;
using Pixman;

namespace Basin.Desktop;

/// <summary>
/// Feeds the blur regions of <c>ext_background_effect_v1</c> to the scene as backdrop regions for one backdrop effect.
/// </summary>
public sealed class BackgroundBlurDriver : ISurfaceBackdrop
{
    private readonly BackgroundEffectManager _manager;
    private readonly IBackdropEffect _effect;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundBlurDriver"/> class.
    /// </summary>
    /// <param name="manager">The manager that holds the committed blur regions.</param>
    /// <param name="effect">The effect to draw behind the regions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="manager"/> or <paramref name="effect"/> is <see langword="null"/>.</exception>
    public BackgroundBlurDriver(BackgroundEffectManager manager, IBackdropEffect effect)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(effect);
        _manager = manager;
        _effect = effect;
    }

    /// <summary>
    /// Gets the effect that the driver was created with. It is never <see langword="null"/>.
    /// </summary>
    public IBackdropEffect? Effect => _effect;

    /// <summary>
    /// Writes the surface's committed blur region, clipped to the surface size.
    /// </summary>
    /// <param name="surface">The surface to query.</param>
    /// <param name="into">The region to fill, in surface-local logical pixels.</param>
    /// <returns><see langword="false"/> when the clipped region is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> or <paramref name="into"/> is <see langword="null"/>.</exception>
    public bool RegionOf(Surface surface, PixmanRegion32 into)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(into);
        if (_manager.BlurRegionOf(surface) is not { IsEmpty: false } region)
        {
            into.Clear();
            return false;
        }

        into.Copy(region);
        into.IntersectRect(into, 0, 0, (uint)Math.Max(0, surface.Current.Width), (uint)Math.Max(0, surface.Current.Height));
        return !into.IsEmpty;
    }

    /// <inheritdoc/>
    public void Forget(object key) => _effect.ForgetSurface(key);
}
