using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs KWin's track mouse effect. While it is held, two half rings turn in opposite directions around the cursor.
/// </summary>
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

    /// <summary>
    /// Gets or sets the radius of the outer ring, in pixels. The inner ring is 0.6 times as large. The default is 24.
    /// </summary>
    public double Radius { get; set; } = 24;

    /// <summary>
    /// Gets or sets the line width, in pixels. The default is 3.
    /// </summary>
    public double LineWidth { get; set; } = 3;

    /// <summary>
    /// Gets or sets the ring color. The default is white.
    /// </summary>
    public RenderColor Color { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>
    /// Gets a value indicating whether the rings show.
    /// </summary>
    public bool IsActive => _held;

    /// <summary>
    /// Gets the turn of the outer ring, in radians. One full turn takes four seconds.
    /// </summary>
    public double Angle => _angle;

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
    /// Releases the mesh in the feedback overlay and hides the rings.
    /// </summary>
    /// <param name="overlay">The overlay that <see cref="Attach"/> received.</param>
    /// <exception cref="ArgumentNullException"><paramref name="overlay"/> is <see langword="null"/>.</exception>
    public void Detach(FeedbackOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _thread.Assert();
        overlay.Release(this);
        _mesh = null;
        _held = false;
    }

    /// <summary>
    /// Shows or hides the rings, for example while a modifier chord is held.
    /// </summary>
    /// <param name="held"><see langword="true"/> to show the rings.</param>
    public void SetHeld(bool held)
    {
        _thread.Assert();
        _held = held;
        Refresh();
    }

    /// <summary>
    /// Moves the center of the rings.
    /// </summary>
    /// <param name="x">The horizontal cursor position, in the coordinates of the overlay.</param>
    /// <param name="y">The vertical cursor position, in the coordinates of the overlay.</param>
    public void SetCursor(double x, double y)
    {
        _thread.Assert();
        _x = x;
        _y = y;
        Refresh();
    }

    /// <summary>
    /// Turns the rings to the angle for the tick time.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while the rings show.</returns>
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

    /// <inheritdoc/>
    public int VertexCount(in Box bounds) => _held ? Arcs * FeedbackShapes.RingVertexCount(Segments) : 0;

    /// <inheritdoc/>
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
