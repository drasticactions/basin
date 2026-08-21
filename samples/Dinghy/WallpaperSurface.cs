using Basin.WindowManager;
using Dinghy.Protocol;
using SkiaSharp;
using Wayland;

namespace Dinghy;

internal sealed class WallpaperSurface : IDisposable
{
    private readonly ManagerSurface _surface;
    private bool _drawn;

    internal WallpaperSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Background, "dinghy-wallpaper");
        _surface.SetAnchor(
            ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom
            | ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public bool Render(int scale)
    {
        if (!_surface.IsConfigured)
        {
            return false;
        }

        var size = _surface.ConfiguredSize;
        if (size.IsEmpty)
        {
            size = Output.Dimensions;
        }

        if (size.IsEmpty)
        {
            return false;
        }

        if (_drawn && _surface.Width == size.Width && _surface.Height == size.Height && _surface.Scale == scale)
        {
            return false;
        }

        var pixels = _surface.Prepare(size.Width, size.Height, scale);
        if (pixels == 0)
        {
            return false;
        }

        using var surface = _surface.CreateCanvas(pixels);
        if (surface is null)
        {
            return false;
        }

        surface.Canvas.Clear(Theme.Color(Theme.DesktopBackground));
        surface.Canvas.Flush();
        _surface.SetInputRegion(new Rect(0, 0, size.Width, size.Height));
        _drawn = true;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Invalidate() => _drawn = false;

    public void Dispose() => _surface.Dispose();
}
