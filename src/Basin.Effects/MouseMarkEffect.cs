using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs the mouse mark effect. It keeps freehand lines and arrows that the user draws in the feedback overlay until they are cleared.
/// </summary>
public sealed class MouseMarkEffect : IMeshSource
{
    /// <summary>
    /// The default value of <see cref="LineWidth"/>, in pixels.
    /// </summary>
    public const double DefaultLineWidth = 3;

    private const double ArrowHeadLength = 30;

    private const double ArrowHeadSpread = 0.4;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<List<(double X, double Y)>> _marks = [];
    private readonly List<(double X, double Y)> _drawing = [];
    private SceneMesh? _mesh;
    private (double X, double Y)? _arrowStart;
    private double _lineWidth = DefaultLineWidth;
    private RenderColor _color = new(1f, 0f, 0f, 1f);

    /// <summary>
    /// Gets or sets the line width, in pixels. A change shows at the next frame.
    /// </summary>
    public double LineWidth
    {
        get => _lineWidth;
        set
        {
            _thread.Assert();
            if (_lineWidth != value)
            {
                _lineWidth = value;
                Refresh();
            }
        }
    }

    /// <summary>
    /// Gets or sets the line color. The default is red. A change shows at the next frame.
    /// </summary>
    public RenderColor Color
    {
        get => _color;
        set
        {
            _thread.Assert();
            if (_color != value)
            {
                _color = value;
                Refresh();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether there is a finished mark or a line in progress.
    /// </summary>
    public bool IsActive => _marks.Count > 0 || _drawing.Count > 0;

    /// <summary>
    /// Gets a value indicating whether a freehand line is in progress.
    /// </summary>
    public bool IsDrawing => _drawing.Count > 0;

    /// <summary>
    /// Gets a value indicating whether an arrow has a start point and waits for <see cref="EndArrow"/>.
    /// </summary>
    public bool IsArrowing => _arrowStart is not null;

    /// <summary>
    /// Claims a mesh in the feedback overlay and draws into it from now on.
    /// </summary>
    /// <param name="overlay">The feedback overlay of the output.</param>
    /// <exception cref="ArgumentNullException"><paramref name="overlay"/> is <see langword="null"/>.</exception>
    public void Attach(FeedbackOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _thread.Assert();
        _mesh = overlay.Claim(this, this);
    }

    /// <summary>
    /// Releases the mesh in the feedback overlay and removes every mark.
    /// </summary>
    /// <param name="overlay">The overlay that <see cref="Attach"/> received.</param>
    /// <exception cref="ArgumentNullException"><paramref name="overlay"/> is <see langword="null"/>.</exception>
    public void Detach(FeedbackOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _thread.Assert();
        overlay.Release(this);
        _mesh = null;
        Clear();
    }

    /// <summary>
    /// Starts a freehand line at a point and drops a line that was in progress.
    /// </summary>
    /// <param name="x">The horizontal position, in the coordinates of the overlay.</param>
    /// <param name="y">The vertical position, in the coordinates of the overlay.</param>
    public void BeginFreehand(double x, double y)
    {
        _thread.Assert();
        _drawing.Clear();
        _drawing.Add((x, y));
        Refresh();
    }

    /// <summary>
    /// Adds a point to the freehand line in progress. It does nothing when no line is in progress.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    public void Extend(double x, double y)
    {
        _thread.Assert();
        if (_drawing.Count > 0)
        {
            _drawing.Add((x, y));
            Refresh();
        }
    }

    /// <summary>
    /// Keeps the freehand line in progress as a mark when it has at least two points.
    /// </summary>
    public void EndFreehand()
    {
        _thread.Assert();
        if (_drawing.Count > 1)
        {
            _marks.Add([.. _drawing]);
        }

        _drawing.Clear();
        Refresh();
    }

    /// <summary>
    /// Sets the start point of an arrow. Nothing draws until <see cref="EndArrow"/>.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    public void BeginArrow(double x, double y)
    {
        _thread.Assert();
        _arrowStart = (x, y);
    }

    /// <summary>
    /// Adds an arrow from the start point to a point. An arrow shorter than one pixel, or one with no start point, adds nothing.
    /// </summary>
    /// <param name="x">The horizontal position of the arrow head.</param>
    /// <param name="y">The vertical position of the arrow head.</param>
    public void EndArrow(double x, double y)
    {
        _thread.Assert();
        if (_arrowStart is not { } start)
        {
            return;
        }

        _arrowStart = null;
        var dx = x - start.X;
        var dy = y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1)
        {
            return;
        }

        var angle = Math.Atan2(dy, dx);
        var head = Math.Min(ArrowHeadLength, length / 2);
        _marks.Add(
        [
            (start.X, start.Y),
            (x, y),
            (x - (Math.Cos(angle - ArrowHeadSpread) * head), y - (Math.Sin(angle - ArrowHeadSpread) * head)),
            (x, y),
            (x - (Math.Cos(angle + ArrowHeadSpread) * head), y - (Math.Sin(angle + ArrowHeadSpread) * head)),
        ]);
        Refresh();
    }

    /// <summary>
    /// Removes the last finished mark.
    /// </summary>
    public void UndoLast()
    {
        _thread.Assert();
        if (_marks.Count > 0)
        {
            _marks.RemoveAt(_marks.Count - 1);
            Refresh();
        }
    }

    /// <summary>
    /// Removes every mark, the line in progress and the arrow start.
    /// </summary>
    public void Clear()
    {
        _thread.Assert();
        _marks.Clear();
        _drawing.Clear();
        _arrowStart = null;
        Refresh();
    }

    /// <inheritdoc/>
    public int VertexCount(in Box bounds)
    {
        var segments = 0;
        foreach (var mark in _marks)
        {
            segments += Math.Max(0, mark.Count - 1);
        }

        segments += Math.Max(0, _drawing.Count - 1);
        return segments * FeedbackShapes.QuadVertexCount;
    }

    /// <inheritdoc/>
    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        var color = FeedbackShapes.Premultiplied(Color, 1.0);
        var write = 0;
        foreach (var mark in _marks)
        {
            write += WriteMark(mark, into[write..], color);
        }

        WriteMark(_drawing, into[write..], color);
    }

    private int WriteMark(List<(double X, double Y)> mark, Span<MeshVertex> into, in RenderColor color)
    {
        var write = 0;
        for (var i = 1; i < mark.Count; i++)
        {
            FeedbackShapes.WriteLine(
                into.Slice(write, 6), mark[i - 1].X, mark[i - 1].Y, mark[i].X, mark[i].Y, LineWidth, color);
            write += 6;
        }

        return write;
    }

    private void Refresh()
    {
        if (_mesh is not { IsDestroyed: false } mesh)
        {
            return;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        var any = false;
        foreach (var mark in _marks)
        {
            foreach (var (x, y) in mark)
            {
                any = true;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        foreach (var (x, y) in _drawing)
        {
            any = true;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        if (!any)
        {
            mesh.Bounds = default;
            mesh.NotifyMeshChanged();
            return;
        }

        var pad = LineWidth + 2;
        mesh.Bounds = new Box(
            (int)(left - pad), (int)(top - pad), (int)(right - left + (2 * pad)) + 1, (int)(bottom - top + (2 * pad)) + 1);
        mesh.NotifyMeshChanged();
    }
}
