namespace Basin.SoftKeyboard;

/// <summary>
/// Drives <see cref="SoftKeyboardEngine.Tick"/> from one event-loop timer that follows <see cref="SoftKeyboardEngine.NextDeadline"/>.
/// </summary>
public sealed class SoftKeyboardTimer : IDisposable
{
    private readonly SoftKeyboardEngine _engine;
    private readonly Func<long> _clock;
    private readonly IEventSource _timer;
    private long _armed = long.MaxValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftKeyboardTimer"/> class. It adds a timer to the loop and subscribes to <see cref="SoftKeyboardEngine.DeadlineChanged"/>.
    /// </summary>
    /// <param name="loop">The compositor loop that owns the timer.</param>
    /// <param name="engine">The engine to tick.</param>
    /// <param name="clock">The clock in milliseconds that the engine uses, or <see langword="null"/> for <see cref="Environment.TickCount64"/>.</param>
    public SoftKeyboardTimer(ICompositorEventLoop loop, SoftKeyboardEngine engine, Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _clock = clock ?? (static () => Environment.TickCount64);
        _timer = loop.AddTimer(OnTimer);
        engine.DeadlineChanged += Rearm;
    }

    /// <summary>
    /// Removes the timer from the loop and unsubscribes from the engine.
    /// </summary>
    public void Dispose()
    {
        _engine.DeadlineChanged -= Rearm;
        _timer.Remove();
    }

    private void OnTimer()
    {
        _armed = long.MaxValue;
        _engine.Tick(_clock());
        Rearm();
    }

    private void Rearm()
    {
        var deadline = _engine.NextDeadline;
        if (deadline == _armed)
        {
            return;
        }

        _armed = deadline;
        if (deadline == long.MaxValue)
        {
            _timer.UpdateTimer(0);
            return;
        }

        var delay = deadline - _clock();
        _timer.UpdateTimer((int)Math.Clamp(delay, 1, int.MaxValue));
    }
}
