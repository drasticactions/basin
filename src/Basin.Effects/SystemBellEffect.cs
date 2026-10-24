using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Flashes an area with a color when the system bell rings. A bell during a flash is dropped, so a bell in a tight loop gives one steady wash and not a strobe.
/// </summary>
public sealed class SystemBellEffect : IMeshSource
{
    /// <summary>
    /// The default value of <see cref="PauseMillis"/>.
    /// </summary>
    public const double DefaultPauseMillis = 500;

    /// <summary>
    /// The shortest flash, in milliseconds. A shorter <see cref="PauseMillis"/> uses this value.
    /// </summary>
    public const double MinimumPauseMillis = 200;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private SceneMesh? _mesh;
    private Box _area;
    private long _endsNanos;
    private bool _running;

    /// <summary>
    /// Gets or sets the length of a flash, in milliseconds. It is at least <see cref="MinimumPauseMillis"/>.
    /// </summary>
    public double PauseMillis { get; set; } = DefaultPauseMillis;

    /// <summary>
    /// Gets or sets the flash color. The default is red.
    /// </summary>
    public RenderColor Color { get; set; } = new(1f, 0f, 0f, 1f);

    /// <summary>
    /// Gets or sets the flash opacity, clamped between 0 and 1 when drawn. The default is 0.5.
    /// </summary>
    public double Opacity { get; set; } = 0.5;

    /// <summary>
    /// Gets a value indicating whether a flash shows.
    /// </summary>
    public bool IsActive => _running;

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
    /// Releases the mesh in the feedback overlay and stops the flash.
    /// </summary>
    /// <param name="overlay">The overlay that <see cref="Attach"/> received.</param>
    /// <exception cref="ArgumentNullException"><paramref name="overlay"/> is <see langword="null"/>.</exception>
    public void Detach(FeedbackOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _thread.Assert();
        overlay.Release(this);
        _mesh = null;
        _running = false;
    }

    /// <summary>
    /// Starts a flash over an area.
    /// </summary>
    /// <param name="area">The area to flash, in the coordinates of the overlay, for example an output or a window.</param>
    /// <param name="now">The tick of the bell.</param>
    /// <returns><see langword="false"/> when the area is empty or a flash still shows.</returns>
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

    /// <summary>
    /// Ends the flash when its time is over.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while the flash shows.</returns>
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

    /// <inheritdoc/>
    public int VertexCount(in Box bounds) => _running ? FeedbackShapes.QuadVertexCount : 0;

    /// <inheritdoc/>
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
