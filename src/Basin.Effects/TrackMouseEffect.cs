using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class TrackMouseEffect : IMeshSource
{
    private const int Segments = 64;

    private const int Arcs = 2;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private SceneMesh? _mesh;
    private double _x;
    private double _y;
    private double _angle;
    private bool _held;

    public double Radius { get; set; } = 24;

    public double LineWidth { get; set; } = 3;

    public RenderColor Color { get; set; } = new(1f, 1f, 1f, 1f);

    public bool IsActive => _held;

    public double Angle => _angle;

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
        _held = false;
    }

    public void SetHeld(bool held)
    {
        _thread.Assert();
        _held = held;
        Refresh();
    }

    public void SetCursor(double x, double y)
    {
        _thread.Assert();
        _x = x;
        _y = y;
        Refresh();
    }

    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (!_held)
        {
            return false;
        }

        var seconds = tick.TargetPresentNanos / 1_000_000_000.0;
        _angle = ((seconds % 4.0) * 90.0) * Math.PI / 180.0;
        Refresh();
        return true;
    }

    public int VertexCount(in Box bounds) => _held ? Arcs * FeedbackShapes.RingVertexCount(Segments) : 0;

    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        if (!_held)
        {
            return;
        }

        var perRing = FeedbackShapes.RingVertexCount(Segments);
        for (var arc = 0; arc < Arcs; arc++)
        {
            var slice = into.Slice(arc * perRing, perRing);
            var spin = arc == 0 ? _angle : -_angle;
            var radius = Radius * (arc == 0 ? 1.0 : 0.6);
            WriteArc(slice, _x, _y, radius, spin);
        }
    }

    private void WriteArc(Span<MeshVertex> into, double centerX, double centerY, double radius, double spin)
    {
        var half = Math.Max(0.5, LineWidth / 2.0);
        var inner = Math.Max(0, radius - half);
        var outer = radius + half;
        for (var i = 0; i < Segments; i++)
        {
            var fraction = i / (double)Segments;
            var a0 = spin + (fraction * Math.PI * 2);
            var a1 = spin + ((i + 1) / (double)Segments * Math.PI * 2);
            var visible = fraction < 0.5;
            var color = visible ? FeedbackShapes.Premultiplied(Color, 1.0) : default;
            FeedbackShapes.WriteQuad(
                into.Slice(i * 6, 6),
                centerX + (Math.Cos(a0) * inner), centerY + (Math.Sin(a0) * inner),
                centerX + (Math.Cos(a1) * inner), centerY + (Math.Sin(a1) * inner),
                centerX + (Math.Cos(a1) * outer), centerY + (Math.Sin(a1) * outer),
                centerX + (Math.Cos(a0) * outer), centerY + (Math.Sin(a0) * outer),
                color);
        }
    }

    private void Refresh()
    {
        if (_mesh is { IsDestroyed: false } mesh)
        {
            var reach = Radius + LineWidth + 2;
            mesh.Bounds = _held
                ? new Box((int)(_x - reach), (int)(_y - reach), (int)(reach * 2) + 1, (int)(reach * 2) + 1)
                : default;
            mesh.NotifyMeshChanged();
        }
    }
}
