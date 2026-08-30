using Basin;
using Basin.Effects;
using Basin.Scene;

namespace TinyComp;

internal sealed class FeedbackEffects : IDisposable
{
    private readonly FeedbackOverlay _overlay;
    private MouseClickEffect? _clicks;
    private TouchPointsEffect? _touches;
    private TrackMouseEffect? _track;
    private MouseMarkEffect? _marks;
    private StartupFeedbackEffect? _startup;
    private SystemBellEffect? _bell;
    private ShakeCursorEffect? _shake;
    private Basin.Seat.ShakeDetector? _detector;

    public FeedbackEffects(SceneTree layer) => _overlay = new FeedbackOverlay(layer);

    public Action<double>? MagnificationChanged { get; set; }

    public bool MarksEnabled => _marks is not null;

    public bool Any =>
        _clicks is not null || _touches is not null || _track is not null || _marks is not null
        || _startup is not null || _bell is not null || _shake is not null;

    public void Configure(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Toggle(config.MouseClick, ref _clicks, static () => new MouseClickEffect(), e => e.Attach(_overlay), e => e.Detach(_overlay));
        Toggle(config.TouchPoints, ref _touches, static () => new TouchPointsEffect(), e => e.Attach(_overlay), e => e.Detach(_overlay));
        Toggle(config.TrackMouse, ref _track, static () => new TrackMouseEffect(), e => e.Attach(_overlay), e => e.Detach(_overlay));
        Toggle(config.MouseMark, ref _marks, static () => new MouseMarkEffect(), e => e.Attach(_overlay), e => e.Detach(_overlay));
        Toggle(config.SystemBell, ref _bell, static () => new SystemBellEffect(), e => e.Attach(_overlay), e => e.Detach(_overlay));

        var wantsStartup = config.StartupFeedback != StartupFeedbackKind.None;
        Toggle(wantsStartup, ref _startup, static () => new StartupFeedbackEffect(), e => e.Attach(_overlay), e => e.Detach(_overlay));
        if (_startup is { } startup)
        {
            startup.Kind = config.StartupFeedback;
        }

        if (config.ShakeCursor)
        {
            _shake ??= new ShakeCursorEffect();
            _detector ??= new Basin.Seat.ShakeDetector();
        }
        else
        {
            _shake = null;
            _detector = null;
        }
    }

    public void OnButton(double x, double y, uint button, bool pressed, in FrameTick tick)
    {
        if (pressed)
        {
            _clicks?.Click(x, y, button, tick);
        }

        _track?.SetHeld(pressed);
    }

    public void OnMotion(double x, double y, long nanos, in FrameTick tick)
    {
        _track?.SetCursor(x, y);
        _startup?.SetCursor(x, y);
        if (_marks is { IsDrawing: true } marks)
        {
            marks.Extend(x, y);
        }

        if (_detector is { } detector && detector.Update(x, y, nanos))
        {
            _shake?.Shake(tick);
        }
    }

    public void OnTouchDown(int id, double x, double y, in FrameTick tick) => _touches?.Down(id, x, y, tick);

    public void OnTouchMotion(int id, double x, double y, in FrameTick tick) => _touches?.Motion(id, x, y, tick);

    public void OnTouchUp(int id, in FrameTick tick) => _touches?.Up(id, tick);

    public void OnSpawn(in FrameTick tick) => _startup?.Start(tick);

    public void OnMapped() => _startup?.Stop();

    public bool Bell(in Box area, in FrameTick tick) => _bell?.Flash(area, tick) == true;

    public void BeginMark(double x, double y) => _marks?.BeginFreehand(x, y);

    public void EndMark() => _marks?.EndFreehand();

    public void UndoMark() => _marks?.UndoLast();

    public void ClearMarks() => _marks?.Clear();

    public bool Step(in FrameTick tick)
    {
        var running = false;
        if (_clicks is { IsActive: true } clicks)
        {
            running |= clicks.Step(tick);
        }

        if (_touches is { IsActive: true } touches)
        {
            running |= touches.Step(tick);
        }

        if (_track is { } track)
        {
            running |= track.Step(tick);
        }

        if (_startup is { IsActive: true } startup)
        {
            running |= startup.Step(tick);
        }

        if (_bell is { IsActive: true } bell)
        {
            running |= bell.Step(tick);
        }

        if (_shake is { } shake)
        {
            var active = shake.Step(tick);
            MagnificationChanged?.Invoke(shake.Magnification);
            running |= active;
        }

        return running;
    }

    public void Dispose()
    {
        _clicks?.Detach(_overlay);
        _touches?.Detach(_overlay);
        _track?.Detach(_overlay);
        _marks?.Detach(_overlay);
        _startup?.Detach(_overlay);
        _bell?.Detach(_overlay);
        _overlay.Dispose();
    }

    private static void Toggle<T>(bool wanted, ref T? slot, Func<T> create, Action<T> attach, Action<T> detach)
        where T : class
    {
        if (wanted && slot is null)
        {
            slot = create();
            attach(slot);
        }
        else if (!wanted && slot is { } present)
        {
            detach(present);
            slot = null;
        }
    }
}
