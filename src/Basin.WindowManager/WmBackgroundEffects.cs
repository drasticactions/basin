using Basin.WindowManager.Protocol;
using Pixman;
using Wayland;

namespace Basin.WindowManager;

/// <summary>
/// A client for <c>ext_background_effect_manager_v1</c>, which blurs what is behind the manager's own translucent surfaces. It is present even when the compositor does not serve the global.
/// </summary>
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

    /// <summary>
    /// Gets a value indicating whether the compositor serves the global. It is settled when the <see cref="RiverWindowManager"/> constructor returns.
    /// </summary>
    public bool Bound => _manager is not null;

    /// <summary>
    /// Gets a value indicating whether the compositor promised blur in its capabilities event. It is settled when the <see cref="RiverWindowManager"/> constructor returns.
    /// </summary>
    public bool Supported => (_capabilities & ExtBackgroundEffectManagerV1.Capability.Blur) != 0;

    /// <summary>
    /// Sets or unsets the blur region of a surface. It applies on the surface's next commit. The per-surface object is made on first use.
    /// </summary>
    /// <param name="surface">The surface to blur behind.</param>
    /// <param name="rects">The region in surface-local logical coordinates. Empty boxes are skipped. An empty span unsets the region.</param>
    /// <returns>The value of <see cref="Supported"/>, or <see langword="false"/> when the global or <c>wl_compositor</c> is missing or the object is disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Sets or unsets the blur region of a surface from a pixman region. It applies on the surface's next commit.
    /// </summary>
    /// <param name="surface">The surface to blur behind.</param>
    /// <param name="region">The region in surface-local logical coordinates. <see langword="null"/> or an empty region unsets it.</param>
    /// <returns>The value of <see cref="Supported"/>, or <see langword="false"/> when the global or <c>wl_compositor</c> is missing or the object is disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Destroys the per-surface object, which clears the region on the next commit. Call it before the surface is destroyed.
    /// </summary>
    /// <param name="surface">The surface to forget. A surface with no object is ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public void Forget(WlSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (_surfaces.Remove(surface, out var effect) && !effect.IsDestroyed)
        {
            effect.Destroy();
        }
    }

    /// <summary>
    /// Destroys every per-surface object and the global binding. A second call does nothing.
    /// </summary>
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
