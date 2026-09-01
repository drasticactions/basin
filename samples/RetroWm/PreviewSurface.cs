using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using Wayland;

namespace RetroWm;

internal sealed class PreviewSurface : IDisposable
{
    private const byte FillAlpha = 90;

    private readonly ManagerSurface _surface;
    private Rect _rect;
    private int _lastScale = -1;
    private uint _lastColor;
    private bool _started;
    private bool _drawn;

    internal PreviewSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Top, "retro-wm-drop-preview");
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

        var color = Theme.DropPreview;
        var redraw = !_drawn || rect.Width != _rect.Width || rect.Height != _rect.Height
            || scale != _lastScale || color != _lastColor;
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

            surface.Canvas.Clear(Theme.Color(color).WithAlpha(FillAlpha));
            surface.Canvas.Flush();
            _surface.SetInputRegion(Rect.Empty);
            _drawn = true;
            _lastScale = scale;
            _lastColor = color;
        }

        _surface.SetMargin(rect.Y, rect.X);
        _surface.Commit();
    }

    public void Dispose() => _surface.Dispose();
}
