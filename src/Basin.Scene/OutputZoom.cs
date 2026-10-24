using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Scene;

/// <summary>
/// The default <see cref="IOutputZoom"/>. It gives every output of a scene one factor, pans the output under the pointer at its edges,
/// and follows the focus and the caret that each tracked <see cref="IAccessibleFocus"/> reports. Use it on the compositor thread.
/// </summary>
public sealed class OutputZoom : IOutputZoom, IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Scene _scene;
    private readonly LayoutPointer _pointer;
    private readonly List<IAccessibleFocus> _sources = [];
    private double _factor = 1.0;
    private double _lastOn = 2.0;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputZoom"/> class. Zoom starts off.
    /// </summary>
    /// <param name="scene">The scene whose outputs zoom.</param>
    /// <param name="pointer">The layout pointer that the outputs follow.</param>
    public OutputZoom(Scene scene, LayoutPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(pointer);
        _scene = scene;
        _pointer = pointer;
        _pointer.Moved += OnPointerMoved;
    }

    /// <inheritdoc/>
    public double Factor => _factor;

    /// <inheritdoc/>
    public ZoomFollow Follow { get; set; } = ZoomFollow.All;

    /// <summary>
    /// Gets or sets the ratio that <see cref="ZoomIn"/> and <see cref="ZoomOut"/> apply. The default is 1.25.
    /// </summary>
    public double Step { get; set; } = 1.25;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>
    /// Follows the focus and the caret that a source reports, until <see cref="Untrack"/> or disposal. Several sources can be tracked. A source tracked twice counts once.
    /// </summary>
    /// <param name="focus">The source, or <see langword="null"/> to do nothing.</param>
    public void Track(IAccessibleFocus? focus)
    {
        _thread.Assert();
        if (_disposed || focus is null || _sources.Contains(focus))
        {
            return;
        }

        _sources.Add(focus);
        focus.Moved += OnFocusMoved;
    }

    /// <summary>
    /// Stops following a source. A source that is not tracked is ignored.
    /// </summary>
    /// <param name="focus">The source.</param>
    public void Untrack(IAccessibleFocus focus)
    {
        _thread.Assert();
        if (_sources.Remove(focus))
        {
            focus.Moved -= OnFocusMoved;
        }
    }

    /// <inheritdoc/>
    public void Toggle() => SetFactor(_factor > 1.0 ? 1.0 : _lastOn);

    /// <inheritdoc/>
    public void ZoomIn() => SetFactor(_factor * Step);

    /// <inheritdoc/>
    public void ZoomOut()
    {
        var next = _factor / Step;
        SetFactor(next < Step ? 1.0 : next);
    }

    /// <inheritdoc/>
    public void SetFactor(double factor)
    {
        _thread.Assert();
        factor = double.IsFinite(factor) ? Math.Clamp(Math.Round(factor, 4), 1.0, SceneOutput.MaxZoom) : 1.0;
        if (_disposed || factor == _factor)
        {
            return;
        }

        _factor = factor;
        if (factor >= Step)
        {
            _lastOn = factor;
        }

        foreach (var output in _scene.Zoomables)
        {
            Apply(output);
        }

        Changed?.Invoke();
    }

    /// <inheritdoc/>
    public void Show(in Box box, ZoomFollow source)
    {
        _thread.Assert();
        if (_disposed || _factor <= 1.0 || (Follow & source) == 0)
        {
            return;
        }

        var centerX = box.X + (box.Width / 2.0);
        var centerY = box.Y + (box.Height / 2.0);
        if (OutputAt(centerX, centerY) is not { } output)
        {
            return;
        }

        Sync(output);
        var (x, y, width, height) = output.ZoomArea;
        var left = Reveal(x, width, box.X, box.Width);
        var top = Reveal(y, height, box.Y, box.Height);
        output.SetZoom(_factor, left + (width / 2), top + (height / 2));
        SceneLog.Log.Debug($"zoom: {source} shows {box.X},{box.Y} {box.Width}x{box.Height}, the area starts at {left:0},{top:0}");
    }

    /// <summary>
    /// Stops following the pointer and the focus source. Outputs keep their zoom.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pointer.Moved -= OnPointerMoved;
        foreach (var source in _sources)
        {
            source.Moved -= OnFocusMoved;
        }

        _sources.Clear();
    }

    private static double Reveal(double start, double length, double boxStart, double boxLength)
    {
        if (boxLength >= length || boxStart < start)
        {
            return boxStart;
        }

        return boxStart + boxLength > start + length ? boxStart + boxLength - length : start;
    }

    private void Apply(IZoomable output)
    {
        var (x, y, width, height) = output.LogicalArea;
        var inside = _pointer.X >= x && _pointer.Y >= y && _pointer.X < x + width && _pointer.Y < y + height;
        if (inside)
        {
            output.SetZoom(_factor, _pointer.X, _pointer.Y);
            return;
        }

        var (zx, zy, zw, zh) = output.ZoomArea;
        output.SetZoom(_factor, zx + (zw / 2), zy + (zh / 2));
    }

    private void Sync(IZoomable output)
    {
        if (output.ZoomFactor != _factor)
        {
            Apply(output);
        }
    }

    private IZoomable? OutputAt(double px, double py)
    {
        foreach (var output in _scene.Zoomables)
        {
            var (x, y, width, height) = output.LogicalArea;
            if (px >= x && py >= y && px < x + width && py < y + height)
            {
                return output;
            }
        }

        return null;
    }

    private void OnFocusMoved(Box box, AccessibleFocusKind kind) =>
        Show(box, kind == AccessibleFocusKind.Caret ? ZoomFollow.Caret : ZoomFollow.Focus);

    private void OnPointerMoved()
    {
        if (_factor <= 1.0 || (Follow & ZoomFollow.Pointer) == 0)
        {
            return;
        }

        var (px, py) = (_pointer.X, _pointer.Y);
        if (OutputAt(px, py) is not { } output)
        {
            return;
        }

        Sync(output);
        var (x, y, width, height) = output.ZoomArea;
        var left = px < x ? px : px >= x + width ? px - width : x;
        var top = py < y ? py : py >= y + height ? py - height : y;
        if (left != x || top != y)
        {
            output.SetZoom(_factor, left + (width / 2), top + (height / 2));
        }
    }
}
