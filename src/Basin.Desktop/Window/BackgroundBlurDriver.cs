using Basin.Scene;
using Pixman;

namespace Basin.Desktop;

public sealed class BackgroundBlurDriver : ISurfaceBackdrop
{
    private readonly BackgroundEffectManager _manager;
    private readonly IBackdropEffect _effect;

    public BackgroundBlurDriver(BackgroundEffectManager manager, IBackdropEffect effect)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(effect);
        _manager = manager;
        _effect = effect;
    }

    public IBackdropEffect? Effect => _effect;

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

    public void Forget(object key) => _effect.ForgetSurface(key);
}
