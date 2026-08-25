using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class TouchPointsEffect : IMeshSource
{
    private const int Segments = 48;

    public const double DefaultRingLifeMillis = 300;

    public const double DefaultRingMaxSize = 20;

    public const double DefaultLineWidth = 1.0;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<Contact> _contacts = [];
    private SceneMesh? _mesh;
    private long _now;

    public double RingLifeMillis { get; set; } = DefaultRingLifeMillis;

    public double RingMaxSize { get; set; } = DefaultRingMaxSize;

    public double LineWidth { get; set; } = DefaultLineWidth;

    public RenderColor Color { get; set; } = new(1f, 1f, 1f, 1f);

    public bool IsActive => _contacts.Count > 0;

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
        _contacts.Clear();
    }

    public void Down(int id, double x, double y, in FrameTick now)
    {
        _thread.Assert();
        _contacts.Add(new Contact { Id = id, X = x, Y = y, StartedNanos = now.TargetPresentNanos, Held = true });
        Refresh(now.TargetPresentNanos);
    }

    public void Motion(int id, double x, double y, in FrameTick now)
    {
        _thread.Assert();
        for (var i = 0; i < _contacts.Count; i++)
        {
            if (_contacts[i].Id == id && _contacts[i].Held)
            {
                var contact = _contacts[i];
                contact.X = x;
                contact.Y = y;
                _contacts[i] = contact;
            }
        }

        Refresh(now.TargetPresentNanos);
    }

    public void Up(int id, in FrameTick now)
    {
        _thread.Assert();
        for (var i = 0; i < _contacts.Count; i++)
        {
            if (_contacts[i].Id == id && _contacts[i].Held)
            {
                var contact = _contacts[i];
                contact.Held = false;
                contact.StartedNanos = now.TargetPresentNanos;
                _contacts[i] = contact;
            }
        }

        Refresh(now.TargetPresentNanos);
    }

    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        var life = (long)(Math.Max(1, RingLifeMillis) * 1_000_000);
        for (var i = _contacts.Count - 1; i >= 0; i--)
        {
            if (!_contacts[i].Held && tick.TargetPresentNanos - _contacts[i].StartedNanos >= life)
            {
                _contacts.RemoveAt(i);
            }
        }

        Refresh(tick.TargetPresentNanos);
        return IsActive;
    }

    public int VertexCount(in Box bounds) => _contacts.Count * FeedbackShapes.RingVertexCount(Segments);

    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        var perRing = FeedbackShapes.RingVertexCount(Segments);
        var life = Math.Max(1, RingLifeMillis);
        for (var i = 0; i < _contacts.Count; i++)
        {
            var contact = _contacts[i];
            var slice = into.Slice(i * perRing, perRing);
            var progress = contact.Held
                ? 1.0
                : Math.Clamp((_now - contact.StartedNanos) / 1_000_000.0 / life, 0, 1);
            var radius = contact.Held ? RingMaxSize : progress * RingMaxSize;
            var alpha = contact.Held ? 1.0 : 1.0 - progress;
            FeedbackShapes.WriteRing(
                slice, contact.X, contact.Y, radius, LineWidth,
                FeedbackShapes.Premultiplied(Color, alpha), Segments);
        }
    }

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
        if (_contacts.Count == 0)
        {
            return default;
        }

        var reach = RingMaxSize + LineWidth + 2;
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var contact in _contacts)
        {
            left = Math.Min(left, contact.X - reach);
            top = Math.Min(top, contact.Y - reach);
            right = Math.Max(right, contact.X + reach);
            bottom = Math.Max(bottom, contact.Y + reach);
        }

        return new Box((int)left, (int)top, (int)(right - left) + 1, (int)(bottom - top) + 1);
    }

    private struct Contact
    {
        public int Id;
        public double X;
        public double Y;
        public long StartedNanos;
        public bool Held;
    }
}
