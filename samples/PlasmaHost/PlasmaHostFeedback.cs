using Basin;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Effects;
using Basin.Scene;
using Basin.Seat;

namespace PlasmaHost;

internal sealed class PlasmaHostFeedback : IDisposable, IBell
{
    private readonly FeedbackOverlay _overlay;
    private readonly ShakeDetector _shake = new();
    private CursorController? _cursor;

    public PlasmaHostFeedback(KwinEffectsConfig config, SceneTree feedbackLayer)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(feedbackLayer);
        _overlay = new FeedbackOverlay(feedbackLayer);

        _shake.IntervalMillis = config.Integer("shakecursor", "TimeInterval", 1000);
        _shake.Sensitivity = config.Number("shakecursor", "Sensitivity", 4);

        if (config.IsEnabled("shakecursor", true))
        {
            ShakeCursor = new ShakeCursorEffect(new ShakeCursorOptions
            {
                TimeIntervalMillis = _shake.IntervalMillis,
                Sensitivity = _shake.Sensitivity,
                Magnification = config.Number("shakecursor", "Magnification", 3),
                OverMagnification = config.Number("shakecursor", "OverMagnification", 1),
            });
        }

        if (config.IsEnabled("mouseclick", false))
        {
            Clicks = new MouseClickEffect(new MouseClickOptions
            {
                RingLifeMillis = config.Integer("mouseclick", "RingLife", 300),
                RingSize = config.Integer("mouseclick", "RingSize", 20),
                RingCount = config.Integer("mouseclick", "RingCount", 2),
                LineWidth = config.Number("mouseclick", "LineWidth", 1.0),
            });
            Clicks.Attach(_overlay);
        }

        if (config.IsEnabled("mousemark", false))
        {
            Marks = new MouseMarkEffect { LineWidth = config.Integer("mousemark", "LineWidth", 3) };
            Marks.Attach(_overlay);
        }

        if (config.IsEnabled("trackmouse", false))
        {
            Track = new TrackMouseEffect();
            Track.Attach(_overlay);
        }

        if (config.IsEnabled("touchpoints", false))
        {
            Touches = new TouchPointsEffect();
            Touches.Attach(_overlay);
        }

        if (config.IsEnabled("systembell", true))
        {
            Bell = new SystemBellEffect
            {
                PauseMillis = KdeIni.ReadEntry(KdeIni.ConfigPath("kaccessrc"), "Bell", "VisibleBellPause") is { } raw &&
                    double.TryParse(raw, out var pause)
                    ? pause
                    : SystemBellEffect.DefaultPauseMillis,
            };
            Bell.Attach(_overlay);
        }

        if (config.IsEnabled("startupfeedback", true))
        {
            Startup = new StartupFeedbackEffect
            {
                TimeoutSeconds = config.Integer("startupfeedback", "Timeout", 5),
            };
            Startup.Attach(_overlay);
        }
    }

    public ShakeCursorEffect? ShakeCursor { get; }

    public MouseClickEffect? Clicks { get; }

    public MouseMarkEffect? Marks { get; }

    public TrackMouseEffect? Track { get; }

    public TouchPointsEffect? Touches { get; }

    public SystemBellEffect? Bell { get; }

    public StartupFeedbackEffect? Startup { get; }

    public Func<Surface?, Box>? BellArea { get; set; }

    public Func<FrameTick>? Now { get; set; }

    public CursorController? Cursor
    {
        get => _cursor;
        set => _cursor = value;
    }

    public bool MarkChordHeld { get; set; }

    public bool ArrowChordHeld { get; set; }

    public void Ring(Surface? surface)
    {
        var tick = Now?.Invoke() ?? default;
        Bell?.Flash(BellArea?.Invoke(surface) ?? default, tick);
    }

    public void PointerMoved(double x, double y, bool buttonsDown)
    {
        var tick = Now?.Invoke() ?? default;
        Track?.SetCursor(x, y);
        Startup?.SetCursor(x, y);
        if (Marks is { } marks)
        {
            if (MarkChordHeld && !marks.IsDrawing)
            {
                marks.BeginFreehand(x, y);
            }
            else if (MarkChordHeld)
            {
                marks.Extend(x, y);
            }
            else if (marks.IsDrawing)
            {
                marks.EndFreehand();
            }
        }

        if (buttonsDown)
        {
            _shake.Reset();
            return;
        }

        if (_shake.Update(x, y, tick.TargetPresentNanos) && ShakeCursor is { } shake)
        {
            shake.Shake(tick);
        }
    }

    public void PointerButton(double x, double y, uint button, bool pressed)
    {
        var tick = Now?.Invoke() ?? default;
        if (pressed)
        {
            Clicks?.Click(x, y, button, tick);
        }

        if (Marks is { } marks && ArrowChordHeld)
        {
            if (pressed)
            {
                marks.BeginArrow(x, y);
            }
            else
            {
                marks.EndArrow(x, y);
            }
        }
    }

    public void TouchDown(int id, double x, double y)
    {
        Touches?.Down(id, x, y, Now?.Invoke() ?? default);
    }

    public void TouchMotion(int id, double x, double y)
    {
        Touches?.Motion(id, x, y, Now?.Invoke() ?? default);
    }

    public void TouchUp(int id)
    {
        Touches?.Up(id, Now?.Invoke() ?? default);
    }

    public bool Step(in FrameTick tick)
    {
        var running = false;
        if (ShakeCursor is { } shake)
        {
            running |= shake.Step(tick);
            if (_cursor is { } cursor)
            {
                cursor.Magnification = shake.Magnification;
            }
        }

        running |= Clicks?.Step(tick) ?? false;
        running |= Touches?.Step(tick) ?? false;
        running |= Track?.Step(tick) ?? false;
        running |= Bell?.Step(tick) ?? false;
        running |= Startup?.Step(tick) ?? false;
        return running;
    }

    public void Dispose()
    {
        _overlay.Dispose();
    }
}
