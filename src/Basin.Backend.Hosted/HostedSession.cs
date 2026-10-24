using Basin.Capabilities;
using Basin.Scene;
using Wayland.Server;

namespace Basin.Backend.Hosted;

/// <summary>
/// Runs host render frames over many scene outputs with one client dispatch and one flush per frame.
/// A frame is <see cref="BeginFrame"/>, <see cref="CommitOutput"/> per output, then <see cref="EndFrame"/>.
/// </summary>
public sealed class HostedSession : IDisposable
{
    private readonly WlServerDisplay _display;
    private readonly ICompositorEventLoop _loop;
    private readonly List<SceneOutput> _outputs = [];
    private readonly List<SceneOutput> _presented = [];
    private readonly OutputState _state = new();
    private bool _inFrame;
    private bool _suspended;
    private long _predictedNanos;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedSession"/> class.
    /// </summary>
    /// <param name="display">The display whose clients each frame flushes.</param>
    /// <param name="loop">The compositor loop that each frame dispatches.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public HostedSession(WlServerDisplay display, ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);
        _display = display;
        _loop = loop;
    }

    /// <summary>
    /// Gets or sets the dispatch time above which a frame raises <see cref="DispatchOverran"/>. The default is 8 ms.
    /// </summary>
    public TimeSpan DispatchWarning { get; set; } = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// Gets or sets the dispatch time above which a frame raises <see cref="DispatchExceededLimit"/>. The default is 200 ms.
    /// </summary>
    public TimeSpan DispatchLimit { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Occurs when the client dispatch of a frame takes more than <see cref="DispatchWarning"/> but not more than <see cref="DispatchLimit"/>.
    /// The argument is the dispatch time. No client is disconnected.
    /// </summary>
    public event Action<TimeSpan>? DispatchOverran;

    /// <summary>
    /// Occurs when the client dispatch of a frame takes more than <see cref="DispatchLimit"/>.
    /// The argument is the dispatch time. No client is disconnected.
    /// </summary>
    public event Action<TimeSpan>? DispatchExceededLimit;

    /// <summary>
    /// Occurs in <see cref="BeginFrame"/> after the client dispatch and before the frame clock begins the frame.
    /// The tick uses the refresh interval of the first output.
    /// </summary>
    public event Action<FrameTick>? BeforeDispatch;

    /// <summary>
    /// Occurs on <see cref="RequestWakeup"/>. The consumer asks its host toolkit for a frame here.
    /// </summary>
    public event Action? WakeupRequested;

    /// <summary>
    /// Gets or sets the frame clock that each frame begins and ends for its outputs. If <see langword="null"/>, no frame clock is driven.
    /// </summary>
    public IFrameClock? Frames { get; set; }

    /// <summary>
    /// Gets or sets the clock that animations read. Each frame begins a clock frame, and the tick of <see cref="BeforeDispatch"/> has animation time. If <see langword="null"/>, the tick has monotonic time.
    /// </summary>
    public IAnimationClock? AnimationClock { get; set; }

    /// <summary>
    /// Raises <see cref="WakeupRequested"/>.
    /// </summary>
    public void RequestWakeup() => WakeupRequested?.Invoke();

    /// <summary>
    /// Gets the number of <see cref="CommitOutput"/> calls that composited a frame.
    /// </summary>
    public long Composited { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Suspend"/> was called without a later <see cref="Resume"/>.
    /// </summary>
    public bool IsSuspended => _suspended;

    /// <summary>
    /// Gets the target present time of the last frame in monotonic nanoseconds. It is zero before the first frame and after <see cref="Resume"/>.
    /// </summary>
    public long PredictedVblankNanos => _predictedNanos;

    /// <summary>
    /// Marks the session as suspended. The session itself does not stop frames, so the consumer must stop calling them.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void Suspend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _suspended = true;
    }

    /// <summary>
    /// Clears the suspended state and forgets the frame cadence, so that the next frame predicts its target from the current time.
    /// It does nothing when the session is not suspended.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        _predictedNanos = 0;
    }

    /// <summary>
    /// Gets the registered outputs in the order of registration.
    /// </summary>
    public IReadOnlyList<SceneOutput> Outputs => _outputs;

    /// <summary>
    /// Gets a value indicating whether at least one registered output has damage or other work for the next frame.
    /// </summary>
    public bool NeedsRepaint
    {
        get
        {
            foreach (var output in _outputs)
            {
                if (output.NeedsRepaint)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Registers an output for <see cref="Tick"/>, for the frame clock and for <see cref="NeedsRepaint"/>. An output with no animation clock gets <see cref="AnimationClock"/>.
    /// </summary>
    /// <param name="output">The scene output to register.</param>
    /// <exception cref="InvalidOperationException">The output is already registered.</exception>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void AddOutput(SceneOutput output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(output);
        if (_outputs.Contains(output))
        {
            throw new InvalidOperationException("The output is already registered. Register each output once.");
        }

        output.AnimationClock ??= AnimationClock;
        _outputs.Add(output);
    }

    /// <summary>
    /// Removes an output from the registered outputs.
    /// </summary>
    /// <param name="output">The scene output to remove.</param>
    /// <returns><see langword="true"/> when the output was registered.</returns>
    public bool RemoveOutput(SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _outputs.Remove(output);
    }

    /// <summary>
    /// Starts a frame: dispatches the clients once, raises <see cref="BeforeDispatch"/> and begins the frame on <see cref="Frames"/> for each output.
    /// </summary>
    /// <param name="targetPresentNanos">The target present time in monotonic nanoseconds. Zero or less predicts it from the previous frame or from the current time.</param>
    /// <exception cref="InvalidOperationException">The previous frame was not ended.</exception>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void BeginFrame(long targetPresentNanos = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inFrame)
        {
            throw new InvalidOperationException("The previous frame did not end. Call EndFrame before BeginFrame.");
        }

        _inFrame = true;
        _presented.Clear();
        var refresh = _outputs.Count > 0 ? _outputs[0].Output.CurrentMode.RefreshIntervalNanoseconds : 0;
        targetPresentNanos = Predict(targetPresentNanos, refresh);
        AnimationClock?.BeginFrame();
        var tick = new FrameTick(AnimationClock?.FromMonotonic(targetPresentNanos) ?? targetPresentNanos, refresh);

        HostedDispatch.Run(_loop, DispatchWarning, DispatchLimit, DispatchOverran, DispatchExceededLimit);
        BeforeDispatch?.Invoke(tick);
        if (Frames is { } frames)
        {
            foreach (var output in _outputs)
            {
                frames.BeginFrame(output.Output, targetPresentNanos);
            }
        }
    }

    private long Predict(long targetPresentNanos, long refresh)
    {
        if (targetPresentNanos <= 0 && refresh > 0)
        {
            var now = MonotonicClock.Nanos;
            targetPresentNanos = _predictedNanos > 0 && now < _predictedNanos + refresh
                ? _predictedNanos + refresh
                : now + refresh;
        }

        _predictedNanos = targetPresentNanos;
        return targetPresentNanos;
    }

    /// <summary>
    /// Composites one output into a host buffer. Direct scanout and plane offload are always off.
    /// An output that composites is reported to the frame clock in <see cref="EndFrame"/>.
    /// </summary>
    /// <param name="output">The scene output to composite.</param>
    /// <param name="renderer">The renderer that composites the frame.</param>
    /// <param name="target">The host buffer to composite into.</param>
    /// <param name="age">The buffer age. Zero means that the contents are unknown and the whole output is drawn.</param>
    /// <param name="options">The commit options.</param>
    /// <returns><see langword="true"/> when a frame was composited into <paramref name="target"/>.</returns>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public bool CommitOutput(
        SceneOutput output, IRenderer renderer, IBuffer target, int age, in SceneCommitOptions options = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(target);
        _state.Clear();
        var hosted = options with { AllowDirectScanout = false, AllowPlaneOffload = false };
        var composited = output.Commit(renderer, target, age, _state, hosted);
        if (composited)
        {
            Composited++;
            if (!_presented.Contains(output))
            {
                _presented.Add(output);
            }
        }

        return composited;
    }

    /// <summary>
    /// Marks an output as presented in this frame when the host showed its buffer without a commit. It commits the damage ring of the output.
    /// </summary>
    /// <param name="output">The scene output that the host presented.</param>
    /// <exception cref="InvalidOperationException">No frame is in progress.</exception>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void MarkPresented(SceneOutput output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(output);
        if (!_inFrame)
        {
            throw new InvalidOperationException("No frame is in progress. Call MarkPresented between BeginFrame and EndFrame.");
        }

        output.Ring.Commit();
        if (!_presented.Contains(output))
        {
            _presented.Add(output);
        }
    }

    /// <summary>
    /// Ends the frame: ends the frame on <see cref="Frames"/> for each presented output with time zero, runs idle callbacks and flushes the clients.
    /// </summary>
    /// <exception cref="InvalidOperationException">No frame is in progress.</exception>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void EndFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_inFrame)
        {
            throw new InvalidOperationException("No frame is in progress. Call BeginFrame before EndFrame.");
        }

        _inFrame = false;
        if (Frames is { } frames)
        {
            foreach (var output in _presented)
            {
                frames.EndFrame(output.Output, 0);
            }
        }

        _presented.Clear();
        _loop.DispatchIdle();
        _display.FlushClients();
    }

    /// <summary>
    /// Runs a full frame over all registered outputs with buffer age zero. <see cref="EndFrame"/> runs also when a commit throws.
    /// </summary>
    /// <param name="renderer">The renderer that composites the frames.</param>
    /// <param name="targetFor">Gives the host buffer for an output, or <see langword="null"/> to skip that output.</param>
    /// <param name="options">The commit options.</param>
    /// <returns>The number of outputs that composited a frame.</returns>
    public int Tick(IRenderer renderer, Func<SceneOutput, IBuffer?> targetFor, in SceneCommitOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(targetFor);
        BeginFrame(options.TargetPresentNanos);
        var composited = 0;
        try
        {
            foreach (var output in _outputs)
            {
                if (targetFor(output) is { } target && CommitOutput(output, renderer, target, 0, options))
                {
                    composited++;
                }
            }
        }
        finally
        {
            EndFrame();
        }

        return composited;
    }

    /// <summary>
    /// Clears the registered outputs and releases the output state. The outputs, the loop and the display stay with the caller.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _outputs.Clear();
        _state.Dispose();
    }
}
