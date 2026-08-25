using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Pixman;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class ContrastManager : IBackgroundContrast, IDisposable
{
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IBackgroundEffects? _effects;
    private readonly List<SurfaceContrast> _live = [];

    public ContrastManager(WlServerDisplay display, CompositorGlobal compositor, IBackgroundEffects? effects)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _effects = effects;
        _global = display.CreateGlobal(OrgKdeKwinContrastManager.Interface, Version, OnBind);
    }

    public SurfaceContrast? ContrastOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return surface.Current.GetExtension<SurfaceContrast.Attachment>() is
            { Contrast: { IsReleased: false } contrast }
            ? contrast
            : null;
    }

    public bool TryGetContrast(Surface surface, out ContrastParameters parameters)
    {
        parameters = new ContrastParameters(1.0, 1.0, 1.0);
        if (!Backed || ContrastOf(surface) is not { } contrast)
        {
            return false;
        }

        parameters = contrast.Parameters;
        return true;
    }

    public PixmanRegion32? ContrastRegionOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return Backed && ContrastOf(surface) is { WholeSurface: false } contrast ? contrast.Region : null;
    }

    public void Dispose()
    {
        foreach (var contrast in _live)
        {
            contrast.Dispose();
        }

        _live.Clear();
        _global.Dispose();
    }

    private bool Backed =>
        ((_effects?.Supported ?? BackgroundEffects.None) & BackgroundEffects.Contrast) != BackgroundEffects.None;

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinContrastManagerResource(client, version, id);
        manager.Create += (_, e) =>
        {
            var resource = new OrgKdeKwinContrastResource(client, manager.Version, e.Id);
            if (_compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            var contrast = new SurfaceContrast(surface);
            _live.Add(contrast);
            surface.Pending.SetExtension(new SurfaceContrast.Attachment { Contrast = contrast });
            resource.SetRegion += (_, re) => contrast.SetPendingRegion(_compositor.ResolveRegion(re.Region)?.Pixman);
            resource.SetContrast += (_, ce) => contrast.SetPendingContrast(ce.Contrast);
            resource.SetIntensity += (_, ie) => contrast.SetPendingIntensity(ie.Intensity);
            resource.SetSaturation += (_, se) => contrast.SetPendingSaturation(se.Saturation);
            resource.SetFrost += (_, fe) => contrast.SetPendingFrost(fe.Red, fe.Green, fe.Blue, fe.Alpha);
            resource.UnsetFrost += (_, _) => contrast.UnsetPendingFrost();
            resource.Commit += (_, _) => contrast.Commit();
            resource.Destroyed += (_, _) =>
            {
                contrast.Release();
                _live.Remove(contrast);
                contrast.Dispose();
            };
        };
        manager.Unset += (_, e) =>
        {
            if (_compositor.ResolveSurface(e.Surface) is { } surface)
            {
                surface.Pending.SetExtension(new SurfaceContrast.Attachment());
            }
        };
    }
}
