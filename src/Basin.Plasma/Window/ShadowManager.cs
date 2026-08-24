using Basin.Plasma.Protocol;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class ShadowManager : IDisposable
{
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;

    public ShadowManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _global = display.CreateGlobal(OrgKdeKwinShadowManager.Interface, Version, OnBind);
    }

    public SurfaceShadow? ShadowOf(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return surface.Current.GetExtension<SurfaceShadow.Attachment>() is { Shadow: { IsReleased: false } shadow }
            ? shadow
            : null;
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinShadowManagerResource(client, version, id);
        manager.Create += (_, e) =>
        {
            var resource = new OrgKdeKwinShadowResource(client, manager.Version, e.Id);
            if (_compositor.ResolveSurface(e.Surface) is not { } surface)
            {
                return;
            }

            var shadow = new SurfaceShadow(surface);
            surface.Pending.SetExtension(new SurfaceShadow.Attachment { Shadow = shadow });
            resource.AttachLeft += (_, be) =>
                shadow.AttachPending(ShadowPart.Left, _compositor.Buffers.GetOrImport(be.BufferHandle));
            resource.AttachTopLeft += (_, be) =>
                shadow.AttachPending(ShadowPart.TopLeft, _compositor.Buffers.GetOrImport(be.BufferHandle));
            resource.AttachTop += (_, be) =>
                shadow.AttachPending(ShadowPart.Top, _compositor.Buffers.GetOrImport(be.BufferHandle));
            resource.AttachTopRight += (_, be) =>
                shadow.AttachPending(ShadowPart.TopRight, _compositor.Buffers.GetOrImport(be.BufferHandle));
            resource.AttachRight += (_, be) =>
                shadow.AttachPending(ShadowPart.Right, _compositor.Buffers.GetOrImport(be.BufferHandle));
            resource.AttachBottomRight += (_, be) =>
                shadow.AttachPending(ShadowPart.BottomRight, _compositor.Buffers.GetOrImport(be.BufferHandle));
            resource.AttachBottom += (_, be) =>
                shadow.AttachPending(ShadowPart.Bottom, _compositor.Buffers.GetOrImport(be.BufferHandle));
            resource.AttachBottomLeft += (_, be) =>
                shadow.AttachPending(ShadowPart.BottomLeft, _compositor.Buffers.GetOrImport(be.BufferHandle));
            resource.SetLeftOffset += (_, oe) => shadow.SetPendingOffset(0, oe.Offset.ToDouble());
            resource.SetTopOffset += (_, oe) => shadow.SetPendingOffset(1, oe.Offset.ToDouble());
            resource.SetRightOffset += (_, oe) => shadow.SetPendingOffset(2, oe.Offset.ToDouble());
            resource.SetBottomOffset += (_, oe) => shadow.SetPendingOffset(3, oe.Offset.ToDouble());
            resource.Commit += (_, _) => shadow.Commit();
            resource.Destroyed += (_, _) => shadow.Release();
        };
        manager.Unset += (_, e) =>
        {
            if (_compositor.ResolveSurface(e.Surface) is { } surface)
            {
                surface.Pending.SetExtension(new SurfaceShadow.Attachment());
            }
        };
    }
}
