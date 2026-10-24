using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs KWin's startup feedback while an application launches. It draws a plain square beside the cursor, not the application icon, until the launch ends or times out.
/// </summary>
public sealed class StartupFeedbackEffect : IMeshSource
{
    /// <summary>
    /// The length of one bounce, in milliseconds.
    /// </summary>
    public const double BounceDurationMillis = 500;

    /// <summary>
    /// The number of frames in one blink cycle.
    /// </summary>
    public const int BlinkingFrames = 5;

    /// <summary>
    /// The length of one blink frame, in milliseconds.
    /// </summary>
    public const double BlinkingFrameDurationMillis = 100;

    /// <summary>
    /// The default value of <see cref="TimeoutSeconds"/>.
    /// </summary>
    public const double DefaultTimeoutSeconds = 5;

    private static readonly int[] FrameToBlinkingColor = [0, 1, 2, 3, 2, 1];

    private static readonly RenderColor[] BlinkingColors =
    [
        new(0f, 0f, 0f, 1f),
        new(0.34f, 0.34f, 0.34f, 1f),
        new(0.75f, 0.75f, 0.75f, 1f),
        new(1f, 1f, 1f, 1f),
        new(1f, 1f, 1f, 1f),
    ];

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private SceneMesh? _mesh;
    private StartupFeedbackKind _kind = StartupFeedbackKind.Bouncing;
    private double _cursorX;
    private double _cursorY;
    private double _progress;
    private long _startedNanos;
    private long _lastNanos;
    private bool _running;

    /// <summary>
    /// Gets or sets the time, in seconds, after which the feedback stops by itself.
    /// </summary>
    public double TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>
    /// Gets or sets the side of the square and its offset from the cursor, in pixels. The default is 24.
    /// </summary>
    public double IconSize { get; set; } = 24;

    /// <summary>
    /// Gets or sets the kind of feedback. The default is <see cref="StartupFeedbackKind.Bouncing"/>.
    /// </summary>
    public StartupFeedbackKind Kind
    {
        get => _kind;
        set => _kind = value;
    }

    /// <summary>
    /// Gets a value indicating whether the feedback runs and its kind draws something.
    /// </summary>
    public bool IsActive => _running && _kind is StartupFeedbackKind.Bouncing or StartupFeedbackKind.Blinking;

    /// <summary>
    /// Gets the current blink frame, from 0 to <see cref="BlinkingFrames"/> minus 1.
    /// </summary>
    public int Frame { get; private set; }

    /// <summary>
    /// Gets the current height of the bounce, in pixels.
    /// </summary>
    public double BounceOffset { get; private set; }

    /// <summary>
    /// Gets the current squeeze of the bounce, from −12 to 12. A positive value makes the square wider and lower.
    /// </summary>
    public double Squeeze { get; private set; }

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
    /// Releases the mesh in the feedback overlay and stops the feedback.
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
    /// Starts the feedback, or restarts it and its timeout.
    /// </summary>
    /// <param name="now">The tick of the launch.</param>
    public void Start(in FrameTick now)
    {
        _thread.Assert();
        _running = true;
        _progress = 0;
        _startedNanos = now.TargetPresentNanos;
        _lastNanos = now.TargetPresentNanos;
        Refresh();
    }

    /// <summary>
    /// Stops the feedback, for example when the launched window maps.
    /// </summary>
    public void Stop()
    {
        _thread.Assert();
        _running = false;
        Refresh();
    }

    /// <summary>
    /// Moves the feedback with the cursor.
    /// </summary>
    /// <param name="x">The horizontal cursor position, in the coordinates of the overlay.</param>
    /// <param name="y">The vertical cursor position, in the coordinates of the overlay.</param>
    public void SetCursor(double x, double y)
    {
        _thread.Assert();
        _cursorX = x;
        _cursorY = y;
        Refresh();
    }

    /// <summary>
    /// Advances the bounce or the blink by the time since the last tick, and stops the feedback after <see cref="TimeoutSeconds"/>.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns>The value of <see cref="IsActive"/> after the step.</returns>
    public bool Step(in FrameTick tick)
    {
        _thread.Assert();
        if (!_running)
        {
            return false;
        }

        if ((tick.TargetPresentNanos - _startedNanos) / 1_000_000_000.0 >= Math.Max(0, TimeoutSeconds))
        {
            Stop();
            return false;
        }

        var elapsed = (tick.TargetPresentNanos - _lastNanos) / 1_000_000.0;
        _lastNanos = tick.TargetPresentNanos;
        switch (_kind)
        {
            case StartupFeedbackKind.Bouncing:
            {
                _progress = (_progress + elapsed) % BounceDurationMillis;
                var ratio = _progress / BounceDurationMillis;
                var cosine = Math.Cos((ratio - 0.25) * Math.PI);
                Squeeze = (cosine * cosine * 24) - 12;
                BounceOffset = (Math.Sin((ratio + 1) * Math.PI) * 24) + 8;
                break;
            }

            case StartupFeedbackKind.Blinking:
            {
                var duration = BlinkingFrameDurationMillis * BlinkingFrames;
                _progress = (_progress + elapsed) % duration;
                Frame = (int)Math.Round(_progress / BlinkingFrameDurationMillis) % BlinkingFrames;
                break;
            }
        }

        Refresh();
        return IsActive;
    }

    /// <inheritdoc/>
    public int VertexCount(in Box bounds) => IsActive ? FeedbackShapes.QuadVertexCount : 0;

    /// <inheritdoc/>
    public void WriteVertices(in Box bounds, Span<MeshVertex> into)
    {
        if (!IsActive)
        {
            return;
        }

        var box = Box();
        var color = _kind == StartupFeedbackKind.Blinking
            ? BlinkingColors[FrameToBlinkingColor[Frame]]
            : new RenderColor(1f, 1f, 1f, 1f);
        FeedbackShapes.WriteQuad(
            into,
            box.left, box.top,
            box.right, box.top,
            box.right, box.bottom,
            box.left, box.bottom,
            FeedbackShapes.Premultiplied(color, 1.0));
    }

    private (double left, double top, double right, double bottom) Box()
    {
        var width = IconSize;
        var height = IconSize;
        if (_kind == StartupFeedbackKind.Bouncing)
        {
            width = IconSize * ((64 + Squeeze) / 64.0);
            height = IconSize * ((64 - Squeeze) / 64.0);
        }

        var left = _cursorX + IconSize;
        var top = _cursorY + IconSize + (_kind == StartupFeedbackKind.Bouncing ? -BounceOffset : 0);
        return (left, top, left + width, top + height);
    }

    private void Refresh()
    {
        if (_mesh is not { IsDestroyed: false } mesh)
        {
            return;
        }

        if (!IsActive)
        {
            mesh.Bounds = default;
            mesh.NotifyMeshChanged();
            return;
        }

        var reach = (IconSize * 2) + 48;
        mesh.Bounds = new Box(
            (int)(_cursorX - 4), (int)(_cursorY - reach), (int)(reach * 2), (int)(reach * 2));
        mesh.NotifyMeshChanged();
    }
}
