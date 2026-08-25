using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class MouseMarkEffect : IMeshSource
{
    public const double DefaultLineWidth = 3;

    private const double ArrowHeadLength = 30;

    private const double ArrowHeadSpread = 0.4;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<List<(double X, double Y)>> _marks = [];
    private readonly List<(double X, double Y)> _drawing = [];
    private SceneMesh? _mesh;
    private (double X, double Y)? _arrowStart;

    public double LineWidth { get; set; } = DefaultLineWidth;

    public RenderColor Color { get; set; } = new(1f, 0f, 0f, 1f);

    public bool IsActive => _marks.Count > 0 || _drawing.Count > 0;

    public bool IsDrawing => _drawing.Count > 0;

    public bool IsArrowing => _arrowStart is not null;

    public void Attach(FeedbackOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _thread.Assert();
        _mesh = overlay.Claim(this, this);
    }

    public void Detach(FeedbackOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _thread.Assert();
        overlay.Release(this);
        _mesh = null;
        Clear();
    }

    public void BeginFreehand(double x, double y)
    {
        _thread.Assert();
        _drawing.Clear();
        _drawing.Add((x, y));
        Refresh();
    }

    public void Extend(double x, double y)
    {
        _thread.Assert();
        if (_drawing.Count > 0)
        {
            _drawing.Add((x, y));
            Refresh();
        }
    }

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

    public void BeginArrow(double x, double y)
    {
        _thread.Assert();
        _arrowStart = (x, y);
    }

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

    public void UndoLast()
    {
        _thread.Assert();
        if (_marks.Count > 0)
        {
            _marks.RemoveAt(_marks.Count - 1);
            Refresh();
        }
    }

    public void Clear()
    {
        _thread.Assert();
        _marks.Clear();
        _drawing.Clear();
        _arrowStart = null;
        Refresh();
    }

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
