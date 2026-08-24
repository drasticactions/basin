using Basin.Plasma.Protocol;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class SlideManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;

    public SlideManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _global = display.CreateGlobal(OrgKdeKwinSlideManager.Interface, Version, OnBind);
    }

    public SurfaceSlide? SlideOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return surface.Current.GetExtension<SurfaceSlide.Attachment>() is { Slide: { IsReleased: false } slide }
            ? slide
            : null;
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinSlideManagerResource(client, version, id);
        manager.Create += (_, e) =>
        {
            var resource = new OrgKdeKwinSlideResource(client, manager.Version, e.Id);
            if (_compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            var slide = new SurfaceSlide(surface);
            surface.Pending.SetExtension(new SurfaceSlide.Attachment { Slide = slide });
            resource.SetLocation += (_, le) => slide.SetPendingLocation(le.Location);
            resource.SetOffset += (_, oe) => slide.SetPendingOffset(oe.Offset);
            resource.Commit += (_, _) => slide.Commit();
            resource.Destroyed += (_, _) => slide.Release();
        };
        manager.Unset += (_, e) =>
        {
            if (_compositor.ResolveSurface(e.Surface) is { } surface)
            {
                surface.Pending.SetExtension(new SurfaceSlide.Attachment());
            }
        };
    }
}
