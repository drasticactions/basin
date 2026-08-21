using Basin.WindowManager;
using Dinghy.Protocol;
using SkiaSharp;
using Wayland;

namespace Dinghy;

internal sealed class ShieldSurface : IDisposable
{
    private readonly ManagerSurface _surface;

    internal ShieldSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput output,
        WmOutput wmOutput,
        RiverWindowManager wm)
    {
        Output = wmOutput;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, output, ZwlrLayerShellV1.Layer.Overlay, "dinghy-shield");
        _surface.SetAnchor(
            ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom
            | ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public void Render(int scale)
    {
        if (!_surface.IsConfigured || _surface.ConfiguredSize.IsEmpty)
        {
            return;
        }

        var size = _surface.ConfiguredSize;
        if (_surface.Width == size.Width && _surface.Height == size.Height && _surface.Scale == scale)
        {
            return;
        }

        if (_surface.Prepare(size.Width, size.Height, scale) == 0)
        {
            return;
        }

        _surface.SetInputRegion(new Rect(0, 0, size.Width, size.Height));
        _surface.Commit();
    }

    public void Dispose() => _surface.Dispose();
}
