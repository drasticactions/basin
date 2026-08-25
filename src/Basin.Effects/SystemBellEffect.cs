using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class SystemBellEffect : IMeshSource
{
    public const double DefaultPauseMillis = 500;

    public const double MinimumPauseMillis = 200;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private SceneMesh? _mesh;
    private Box _area;
    private long _endsNanos;
    private bool _running;

    public double PauseMillis { get; set; } = DefaultPauseMillis;

    public RenderColor Color { get; set; } = new(1f, 0f, 0f, 1f);

    public double Opacity { get; set; } = 0.5;

    public bool IsActive => _running;

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
        _running = false;
    }

    public bool Flash(in Box area, in FrameTick now)
    {
        _thread.Assert();
        if (area.IsEmpty)
        {
            return false;
        }

        if (_running && now.TargetPresentNanos < _endsNanos)
        {
            return false;
        }

        var pause = Math.Max(MinimumPauseMillis, PauseMillis);
        _area = area;
        _endsNanos = now.TargetPresentNanos + (long)(pause * 1_000_000);
        _running = true;
        Refresh();
        return true;
    }

    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (!_running)
        {
            return false;
        }

        if (tick.TargetPresentNanos >= _endsNanos)
        {
            _running = false;
            Refresh();
            return false;
        }

        Refresh();
        return true;
    }

    public int VertexCount(in Box bounds) => _running ? FeedbackShapes.QuadVertexCount : 0;

    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        if (!_running)
        {
            return;
        }

        FeedbackShapes.WriteQuad(
            into,
            _area.X, _area.Y,
            _area.Right, _area.Y,
            _area.Right, _area.Bottom,
            _area.X, _area.Bottom,
            FeedbackShapes.Premultiplied(Color, Math.Clamp(Opacity, 0, 1)));
    }

    private void Refresh()
    {
        if (_mesh is { IsDestroyed: false } mesh)
        {
            mesh.Bounds = _running ? _area : default;
            mesh.NotifyMeshChanged();
        }
    }
}
