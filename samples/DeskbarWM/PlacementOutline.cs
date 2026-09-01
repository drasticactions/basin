using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace DeskbarWm;

internal sealed class PlacementOutline : IDisposable
{
    private readonly ManagerSurface _surface;
    private Rect _lastFrame;

    internal PlacementOutline(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Overlay, "deskbar-outline");
        _surface.SetExclusiveZone(-1);
        _surface.SetAnchor(
            ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom
            | ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public bool Render(Rect frame, int scale)
    {
        if (!_surface.IsConfigured || _surface.ConfiguredSize.IsEmpty)
        {
            return false;
        }

        if (frame == _lastFrame)
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
        using var paint = new SKPaint();
        paint.IsAntialias = false;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = SKColors.Black;
        using var dash = SKPathEffect.CreateDash([2f, 2f], 0f);
        paint.PathEffect = dash;
        var local = new Rect(frame.X - Output.Area.X, frame.Y - Output.Area.Y, frame.Width, frame.Height);
        canvas.Canvas.DrawRect(local.X, local.Y, Math.Max(local.Width - 1, 1), Math.Max(local.Height - 1, 1), paint);
        paint.PathEffect = null;
        canvas.Canvas.Flush();
        _surface.SetInputRegion(Rect.Empty);
        _lastFrame = frame;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Dispose() => _surface.Dispose();
}
