using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace RetroWm;

internal sealed class OutlineSurface : IDisposable
{
    private readonly ManagerSurface _surface;
    private Rect _rect;
    private int _lastScale = -1;
    private bool _started;
    private bool _drawn;

    internal OutlineSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Overlay, "retro-wm-outline");
        _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Left);
        _surface.Configured += wm.RequestManage;
    }

    public WmOutput Output { get; }

    public void Show(Rect rect, int scale)
    {
        scale = Math.Max(scale, 1);
        if (rect.IsEmpty)
        {
            return;
        }

        if (!_started)
        {
            _started = true;
            _rect = rect;
            _surface.SetSize(rect.Width, rect.Height);
            _surface.SetMargin(rect.Y, rect.X);
            _surface.CommitInitial();
            return;
        }

        if (!_surface.IsConfigured)
        {
            _rect = rect;
            return;
        }

        var redraw = !_drawn || rect.Width != _rect.Width || rect.Height != _rect.Height
            || scale != _lastScale;
        _rect = rect;
        if (redraw)
        {
            _surface.SetSize(rect.Width, rect.Height);
            var pixels = _surface.Prepare(rect.Width, rect.Height, scale);
            if (pixels == 0)
            {
                return;
            }

            using var surface = _surface.CreateCanvas(pixels);
            if (surface is null)
            {
                return;
            }

            Draw(surface.Canvas, rect.Width, rect.Height, scale);
            surface.Canvas.Flush();
            _surface.SetInputRegion(Rect.Empty);
            _drawn = true;
            _lastScale = scale;
        }

        _surface.SetMargin(rect.Y, rect.X);
        _surface.Commit();
    }

    public void Dispose() => _surface.Dispose();

    private void Draw(SKCanvas canvas, int width, int height, int scale)
    {
        var w = width * scale;
        var h = height * scale;
        var thickness = 2 * scale;
        var dash = 8 * scale;
        var period = 12 * scale;
        using var paint = new SKPaint();
        paint.IsAntialias = false;
        paint.Color = Theme.Color(Theme.OutlineColor);

        for (var x = 0; x < w; x += period)
        {
            var run = Math.Min(dash, w - x);
            canvas.DrawRect(x, 0, run, thickness, paint);
            canvas.DrawRect(x, h - thickness, run, thickness, paint);
        }

        for (var y = 0; y < h; y += period)
        {
            var run = Math.Min(dash, h - y);
            canvas.DrawRect(0, y, thickness, run, paint);
            canvas.DrawRect(w - thickness, y, thickness, run, paint);
        }
    }
}
