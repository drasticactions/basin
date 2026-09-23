using Basin.WindowManager.Protocol;
using Pixman;
using Wayland;

namespace Basin.WindowManager;

public sealed class WmBackgroundEffects : IDisposable
{
    private ExtBackgroundEffectManagerV1? _manager;
    private WlCompositor? _compositor;
    private readonly Dictionary<WlSurface, ExtBackgroundEffectSurfaceV1> _surfaces = [];
    private ExtBackgroundEffectManagerV1.Capability _capabilities;
    private bool _disposed;

    internal WmBackgroundEffects()
    {
    }

    internal void Bind(ExtBackgroundEffectManagerV1 manager, WlEventQueue settle)
    {
        _manager = manager;
        manager.Capabilities += (_, e) => _capabilities = e.Flags;
        manager.SetQueue(settle);
    }

    internal void Settle(WlEventQueue settle, bool wait)
    {
        if (_manager is not { } manager)
        {
            return;
        }

        if (wait)
        {
            settle.Roundtrip();
        }
        else
        {
            settle.DispatchPending();
        }

        manager.SetQueue(null);
    }

    internal void Attach(WlCompositor? compositor) => _compositor = compositor;

    public bool Bound => _manager is not null;

    public bool Supported => (_capabilities & ExtBackgroundEffectManagerV1.Capability.Blur) != 0;

    public bool SetBlurRegion(WlSurface surface, ReadOnlySpan<Box> rects)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (EffectFor(surface) is not { } effect || _compositor is null)
        {
            return false;
        }

        if (rects.IsEmpty)
        {
            effect.SetBlurRegion(null);
            return Supported;
        }

        var region = _compositor.CreateRegion();
        foreach (var rect in rects)
        {
            if (!rect.IsEmpty)
            {
                region.Add(rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        effect.SetBlurRegion(region);
        region.Destroy();
        return Supported;
    }

    public bool SetBlurRegion(WlSurface surface, PixmanRegion32? region)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (EffectFor(surface) is not { } effect || _compositor is null)
        {
            return false;
        }

        if (region is null || region.IsEmpty)
        {
            effect.SetBlurRegion(null);
            return Supported;
        }

        var wire = _compositor.CreateRegion();
        foreach (var band in RegionRects.Of(region))
        {
            wire.Add(band.X1, band.Y1, band.X2 - band.X1, band.Y2 - band.Y1);
        }

        effect.SetBlurRegion(wire);
        wire.Destroy();
        return Supported;
    }

    public void Forget(WlSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (_surfaces.Remove(surface, out var effect) && !effect.IsDestroyed)
        {
            effect.Destroy();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var effect in _surfaces.Values)
        {
            if (!effect.IsDestroyed)
            {
                effect.Destroy();
            }
        }

        _surfaces.Clear();
        if (_manager is { IsDestroyed: false } manager)
        {
            manager.Destroy();
        }
    }

    private ExtBackgroundEffectSurfaceV1? EffectFor(WlSurface surface)
    {
        if (_disposed || _manager is null)
        {
            return null;
        }

        if (!_surfaces.TryGetValue(surface, out var effect))
        {
            effect = _manager.GetBackgroundEffect(surface);
            _surfaces[surface] = effect;
        }

        return effect;
    }
}
