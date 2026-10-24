using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace Basin.WindowManager.Skia;

/// <summary>
/// A layer surface that shows one rectangle, for example a drag outline or a drop preview. A move changes only its margin, and it takes no input.
/// </summary>
public sealed class OverlayRectSurface : IDisposable
{
    private readonly ManagerSurface _surface;
    private Rect _rect;
    private int _lastScale = -1;
    private bool _started;
    private bool _drawn;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverlayRectSurface"/> class, anchored to the top-left of the output. Each configure asks the manager for a manage sequence.
    /// </summary>
    /// <param name="host">The host that creates the layer surface.</param>
    /// <param name="wlOutput">The bound output to show the surface on.</param>
    /// <param name="output">The river output that <paramref name="wlOutput"/> matches.</param>
    /// <param name="layer">The layer to place the surface in.</param>
    /// <param name="scope">The namespace that the compositor sees.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The host's layer shell is not bound.</exception>
    public OverlayRectSurface(
        ManagerSurfaceHost host,
        WlOutput wlOutput,
        WmOutput output,
        ZwlrLayerShellV1.Layer layer,
        string scope)
    {
        ArgumentNullException.ThrowIfNull(host);
        Output = output;
        _surface = host.Create(wlOutput, layer, scope);
        _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Left);
        _surface.Configured += host.Manager.RequestManage;
    }

    /// <summary>
    /// Gets the river output that the surface shows on.
    /// </summary>
    public WmOutput Output { get; }

    /// <summary>
    /// Occurs when the surface repaints. The arguments are the canvas in buffer pixels, the logical width and height, and the scale.
    /// </summary>
    public event Action<SKCanvas, int, int, int>? Paint;

    /// <summary>
    /// Makes the next <see cref="Show"/> repaint.
    /// </summary>
    public void Invalidate() => _drawn = false;

    /// <summary>
    /// Shows the rectangle. The first call only asks for a configure, and calls before the configure only record the rectangle. It repaints when the size or scale changes or after <see cref="Invalidate"/>.
    /// </summary>
    /// <param name="rect">The rectangle relative to the output's top-left corner, in logical pixels. An empty rectangle is ignored.</param>
    /// <param name="scale">The buffer scale. A value under 1 becomes 1.</param>
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

        var redraw = !_drawn || rect.Width != _rect.Width || rect.Height != _rect.Height || scale != _lastScale;
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

            Paint?.Invoke(surface.Canvas, rect.Width, rect.Height, scale);
            surface.Canvas.Flush();
            _surface.SetInputRegion(Rect.Empty);
            _drawn = true;
            _lastScale = scale;
        }

        _surface.SetMargin(rect.Y, rect.X);
        _surface.Commit();
    }

    /// <summary>
    /// Disposes the layer surface.
    /// </summary>
    public void Dispose() => _surface.Dispose();
}
