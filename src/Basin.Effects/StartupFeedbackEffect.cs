using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class StartupFeedbackEffect : IMeshSource
{
    public const double BounceDurationMillis = 500;

    public const int BlinkingFrames = 5;

    public const double BlinkingFrameDurationMillis = 100;

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

    public double TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public double IconSize { get; set; } = 24;

    public StartupFeedbackKind Kind
    {
        get => _kind;
        set => _kind = value;
    }

    public bool IsActive => _running && _kind is StartupFeedbackKind.Bouncing or StartupFeedbackKind.Blinking;

    public int Frame { get; private set; }

    public double BounceOffset { get; private set; }

    public double Squeeze { get; private set; }

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

    public void Start(in FrameTick now)
    {
        _thread.Assert();
        _running = true;
        _progress = 0;
        _startedNanos = now.TargetPresentNanos;
        _lastNanos = now.TargetPresentNanos;
        Refresh();
    }

    public void Stop()
    {
        _thread.Assert();
        _running = false;
        Refresh();
    }

    public void SetCursor(double x, double y)
    {
        _thread.Assert();
        _cursorX = x;
        _cursorY = y;
        Refresh();
    }

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

    public int VertexCount(in Box bounds) => IsActive ? FeedbackShapes.QuadVertexCount : 0;

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
