using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace RetroWm;

internal sealed class BackgroundSurface : IDisposable
{
    private readonly ManagerSurface _surface;
    private int _drawnScale = -1;
    private Size _drawnSize = new(-1, -1);
    private uint _drawnColor;

    internal BackgroundSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Background, "retro-wm-background");
        _surface.SetAnchor(
            ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom |
            ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        _surface.SetSize(0, 0);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public bool Render(int scale)
    {
        scale = Math.Max(scale, 1);
        if (!_surface.IsConfigured || Theme.DesktopBg is not { } color)
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

        if (scale == _drawnScale && size == _drawnSize && color == _drawnColor)
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

        surface.Canvas.Clear(Theme.Color(color));
        surface.Canvas.Flush();
        _surface.SetInputRegion(default(Rect));

        _drawnScale = scale;
        _drawnSize = size;
        _drawnColor = color;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Invalidate() => _drawnScale = -1;

    public void Dispose() => _surface.Dispose();
}
