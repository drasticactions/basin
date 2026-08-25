using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class MouseClickEffect : IMeshSource
{
    private const int Segments = 48;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly MouseClickOptions _options;
    private readonly List<Ring> _clicks = [];
    private SceneMesh? _mesh;

    public MouseClickEffect(MouseClickOptions options = default) =>
        _options = options == default ? new MouseClickOptions() : options;

    public MouseClickOptions Options => _options;

    public bool IsActive => _clicks.Count > 0;

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
        _clicks.Clear();
    }

    public void Click(double x, double y, uint button, in FrameTick now)
    {
        _thread.Assert();
        _clicks.Add(new Ring
        {
            X = x,
            Y = y,
            Color = ColorFor(button),
            StartedNanos = now.TargetPresentNanos,
        });
        Refresh(now.TargetPresentNanos);
    }

    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        var life = (long)(Math.Max(1, _options.RingLifeMillis) * 1_000_000) * Math.Max(1, _options.RingCount);
        for (var i = _clicks.Count - 1; i >= 0; i--)
        {
            if (tick.TargetPresentNanos - _clicks[i].StartedNanos >= life)
            {
                _clicks.RemoveAt(i);
            }
        }

        Refresh(tick.TargetPresentNanos);
        return IsActive;
    }

    public int VertexCount(in Box bounds) =>
        _clicks.Count * Math.Max(1, _options.RingCount) * FeedbackShapes.RingVertexCount(Segments);

    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        var rings = Math.Max(1, _options.RingCount);
        var perRing = FeedbackShapes.RingVertexCount(Segments);
        var life = Math.Max(1, _options.RingLifeMillis);
        var write = 0;
        foreach (var click in _clicks)
        {
            var elapsed = (_now - click.StartedNanos) / 1_000_000.0;
            for (var ring = 0; ring < rings; ring++)
            {
                var age = elapsed - (ring * life / rings);
                var progress = age / life;
                var slice = into.Slice(write, perRing);
                write += perRing;
                if (progress < 0 || progress > 1)
                {
                    slice.Clear();
                    continue;
                }

                FeedbackShapes.WriteRing(
                    slice,
                    click.X,
                    click.Y,
                    progress * _options.RingSize,
                    _options.LineWidth,
                    FeedbackShapes.Premultiplied(click.Color, 1.0 - progress),
                    Segments);
            }
        }
    }

    private long _now;

    private void Refresh(long nowNanos)
    {
        _now = nowNanos;
        if (_mesh is { IsDestroyed: false } mesh)
        {
            mesh.Bounds = Bounds();
            mesh.NotifyMeshChanged();
        }
    }

    private Box Bounds()
    {
        if (_clicks.Count == 0)
        {
            return default;
        }

        var reach = _options.RingSize + _options.LineWidth + 2;
        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        foreach (var click in _clicks)
        {
            left = Math.Min(left, click.X - reach);
            top = Math.Min(top, click.Y - reach);
            right = Math.Max(right, click.X + reach);
            bottom = Math.Max(bottom, click.Y + reach);
        }

        return new Box((int)left, (int)top, (int)(right - left) + 1, (int)(bottom - top) + 1);
    }

    private RenderColor ColorFor(uint button) => button switch
    {
        0x110 => _options.LeftColor,
        0x112 => _options.MiddleColor,
        _ => _options.RightColor,
    };

    private struct Ring
    {
        public double X;
        public double Y;
        public RenderColor Color;
        public long StartedNanos;
    }
}
