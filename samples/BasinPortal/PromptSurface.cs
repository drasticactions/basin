using Basin;
using Basin.Portal.Client;
using Basin.Portal.Prompts.Skia;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace BasinPortal;

internal sealed class PromptSurface : ISkiaPromptSurface
{
    private readonly PromptHost _host;
    private readonly ManagerSurface _surface;
    private readonly SkiaPromptView _view;
    private readonly int _logicalWidth;
    private readonly int _logicalHeight;
    private readonly ClientOutput _output;
    private readonly bool _cover;
    private bool _disposed;

    public PromptSurface(PromptHost host, WlCompositor compositor, WlShm shm, ZwlrLayerShellV1 layerShell,
        ClientOutput output, SkiaPromptView view, bool cover)
    {
        _host = host;
        _view = view;
        _output = output;
        _cover = cover;
        _logicalWidth = cover ? Math.Max(1, output.CurrentMode.Width) : view.SurfaceWidth;
        _logicalHeight = cover ? Math.Max(1, output.CurrentMode.Height) : view.Height;
        _surface = new ManagerSurface(compositor, shm, layerShell, output.Proxy, ZwlrLayerShellV1.Layer.Overlay, "basin-portal-prompt");
        _surface.SetKeyboardInteractivity(ZwlrLayerSurfaceV1.KeyboardInteractivity.Exclusive);
        if (cover)
        {
            _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom | ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        }
        else
        {
            _surface.SetSize(_logicalWidth, _logicalHeight);
        }

        _surface.Configured += Paint;
        _surface.CommitInitial();
    }

    public uint SurfaceId => _surface.SurfaceId;

    public void Repaint() => Paint();

    public void PointerMove(double x, double y) => Redraw(_view.PointerMove(x, y));

    public void PointerButton(uint button, bool pressed) => Redraw(_view.PointerButton(button, pressed));

    public void PointerAxis(double dx, double dy) => Redraw(_view.PointerAxis(dx, dy));

    public void PointerLeave() => Redraw(_view.PointerLeave());

    public void Key(uint key, bool pressed) => Redraw(_view.Key(key, pressed));

    public void Modifiers(uint depressed, uint latched, uint locked, uint group) =>
        _view.Modifiers(depressed, latched, locked, group);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.Remove(this);
        _surface.Dispose();
    }

    private void Paint()
    {
        if (_disposed || !_surface.IsConfigured)
        {
            return;
        }

        var size = _surface.ConfiguredSize;
        var width = size.Width > 0 ? size.Width : _logicalWidth;
        var height = size.Height > 0 ? size.Height : _logicalHeight;
        var scale = Math.Max(1, (int)Math.Round(_output.Scale));
        var pixels = _surface.Prepare(width, height, scale);
        if (pixels == 0)
        {
            return;
        }

        using var canvas = _surface.CreateCanvas(pixels);
        if (canvas is null)
        {
            return;
        }

        canvas.Canvas.Scale(scale);
        _view.Paint(canvas.Canvas, width, height);
        canvas.Canvas.Flush();
        _surface.Commit();
    }

    private void Redraw(bool dirty)
    {
        if (dirty)
        {
            Paint();
        }
    }
}
