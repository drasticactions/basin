using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace DeskbarWm;

internal sealed class DropHighlight : IDisposable
{
    private readonly ManagerSurface _surface;
    private Rect _lastRegion;

    internal DropHighlight(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Overlay, "deskbar-drop");
        _surface.SetExclusiveZone(-1);
        _surface.SetAnchor(
            ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom
            | ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public bool Render(Rect region, int scale)
    {
        if (!_surface.IsConfigured || _surface.ConfiguredSize.IsEmpty || region == _lastRegion)
        {
            return false;
        }

        var size = _surface.ConfiguredSize;
        var pixels = _surface.Prepare(size.Width, size.Height, scale);
        if (pixels == 0)
        {
            return false;
        }

        using var canvas = _surface.CreateCanvas(pixels);
        if (canvas is null)
        {
            return false;
        }

        canvas.Canvas.Scale(scale);
        canvas.Canvas.Clear(SKColors.Transparent);
        var local = new Rect(
            region.X - Output.Area.X, region.Y - Output.Area.Y, region.Width, region.Height);
        using var paint = new SKPaint();
        paint.IsAntialias = false;
        paint.Color = new SKColor(51, 102, 152, 90);
        canvas.Canvas.DrawRect(local.X, local.Y, local.Width, local.Height, paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 2;
        paint.Color = new SKColor(51, 102, 152);
        canvas.Canvas.DrawRect(local.X + 1, local.Y + 1, local.Width - 2, local.Height - 2, paint);
        paint.Style = SKPaintStyle.Fill;
        canvas.Canvas.Flush();
        _surface.SetInputRegion(Rect.Empty);
        _lastRegion = region;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Dispose() => _surface.Dispose();
}
