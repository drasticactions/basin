namespace Basin.Backend.Headless;

/// <summary>
/// An output with no display. It keeps the last committed buffer and presents on a timer at the mode refresh rate, or on <see cref="StepFrame"/>.
/// </summary>
public sealed class HeadlessOutput : OutputBase, IPresentingOutput
{
    private readonly bool _manualFrameClock;
    private readonly IEventSource? _frameTimer;
    private BufferLock _presented;
    private ulong _sequence;

    internal HeadlessOutput(ICompositorEventLoop loop, string name, OutputMode mode, bool manualFrameClock)
        : base(name)
    {
        _manualFrameClock = manualFrameClock;
        Make = "basin";
        Model = "headless";
        Description = $"headless output {name}";

        if (!manualFrameClock)
        {
            _frameTimer = loop.AddTimer(OnFrameTimer);
        }

        using var initial = new OutputState();
        Commit(initial.SetEnabled(true).SetMode(mode));
    }

    /// <summary>
    /// Gets the buffer of the last commit that set one. The output holds a lock on it until the next buffer or destruction.
    /// </summary>
    public IBuffer? PresentedBuffer => _presented.Buffer;

    /// <summary>
    /// Gets the number of frames that this output presented. It starts at zero.
    /// </summary>
    public ulong Sequence => _sequence;

    /// <inheritdoc/>
    public event Action<ulong, uint, ulong>? PresentedOnScreen;

    /// <summary>
    /// Presents one frame now. It increments <see cref="Sequence"/>, raises <see cref="PresentedOnScreen"/> and then the frame event.
    /// </summary>
    /// <exception cref="InvalidOperationException">The output was not created with the manual frame clock.</exception>
    public void StepFrame()
    {
        if (!_manualFrameClock)
        {
            throw new InvalidOperationException("StepFrame requires the manual frame clock.");
        }

        Present();
    }

    /// <inheritdoc/>
    protected override bool TestCommitCore(OutputState state)
    {
        var mode = (state.Fields & OutputStateFields.Mode) != 0 ? state.Mode : CurrentMode;
        if (mode.Width <= 0 || mode.Height <= 0 || mode.RefreshMilliHz <= 0)
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.Buffer) != 0 &&
            (state.Buffer is null || state.Buffer.Width != mode.Width || state.Buffer.Height != mode.Height))
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    protected override bool CommitCore(OutputState state)
    {
        if ((state.Fields & OutputStateFields.Buffer) != 0)
        {
            var presented = state.Buffer!.Lock();
            _presented.Dispose();
            _presented = presented;
        }

        if (!_manualFrameClock && Enabled)
        {
            ArmFrameTimer();
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void OnDestroy()
    {
        _frameTimer?.Remove();
        _presented.Dispose();
    }

    private long _nextFrameNanos;

    private void ArmFrameTimer()
    {
        var interval = (long)CurrentMode.RefreshIntervalNanoseconds;
        var now = MonotonicClock.Nanos;
        if (_nextFrameNanos == 0 || now - _nextFrameNanos > interval)
        {
            _nextFrameNanos = now + interval;
        }
        else if (_nextFrameNanos <= now)
        {
            _nextFrameNanos += interval;
        }

        _frameTimer!.UpdateTimer((int)Math.Max(1, (_nextFrameNanos - now + 999_999) / 1_000_000));
    }

    private void OnFrameTimer()
    {
        if (Enabled)
        {
            ArmFrameTimer();
        }

        Present();
    }

    private void Present()
    {
        _sequence++;
        PresentedOnScreen?.Invoke(
            (ulong)MonotonicClock.Nanos, CurrentMode.RefreshIntervalNanoseconds, _sequence);
        EmitFrame();
    }
}
