using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Pixman;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class BlurManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IBackgroundEffects? _effects;
    private readonly List<SurfaceBlur> _live = [];

    public BlurManager(WlServerDisplay display, CompositorGlobal compositor, IBackgroundEffects? effects)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _effects = effects;
        _global = display.CreateGlobal(OrgKdeKwinBlurManager.Interface, Version, OnBind);
    }

    public SurfaceBlur? BlurOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return surface.Current.GetExtension<SurfaceBlur.Attachment>() is { Blur: { IsReleased: false } blur }
            ? blur
            : null;
    }

    public PixmanRegion32? BlurRegionOf(Surface surface)
    {
        if (((_effects?.Supported ?? BackgroundEffects.None) & BackgroundEffects.Blur) == BackgroundEffects.None)
        {
            return null;
        }

        return BlurOf(surface) is { } blur && !blur.WholeSurface ? blur.Region : null;
    }

    public void Dispose()
    {
        foreach (var blur in _live)
        {
            blur.Dispose();
        }

        _live.Clear();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinBlurManagerResource(client, version, id);
        manager.Create += (_, e) =>
        {
            var resource = new OrgKdeKwinBlurResource(client, manager.Version, e.Id);
            if (_compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            var blur = new SurfaceBlur(surface);
            _live.Add(blur);
            surface.Pending.SetExtension(new SurfaceBlur.Attachment { Blur = blur });
            resource.SetRegion += (_, re) => blur.SetPendingRegion(_compositor.ResolveRegion(re.Region)?.Pixman);
            resource.Commit += (_, _) => blur.Commit();
            resource.Destroyed += (_, _) =>
            {
                blur.Release();
                _live.Remove(blur);
                blur.Dispose();
            };
        };
        manager.Unset += (_, e) =>
        {
            if (_compositor.ResolveSurface(e.Surface) is { } surface)
            {
                surface.Pending.SetExtension(new SurfaceBlur.Attachment());
            }
        };
    }
}
