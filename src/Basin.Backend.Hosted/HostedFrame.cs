using Basin.Capabilities;
using Basin.Scene;
using Wayland.Server;

namespace Basin.Backend.Hosted;

/// <summary>
/// Runs one host render tick for one <see cref="SceneOutput"/>. Each tick dispatches the clients, composites into the host buffer and flushes the clients.
/// </summary>
public sealed class HostedFrame : IDisposable
{
    private readonly WlServerDisplay _display;
    private readonly ICompositorEventLoop _loop;
    private readonly SceneOutput _output;
    private readonly OutputState _state = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedFrame"/> class.
    /// </summary>
    /// <param name="display">The display whose clients each tick flushes.</param>
    /// <param name="loop">The compositor loop that each tick dispatches.</param>
    /// <param name="output">The scene output that each tick composites.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public HostedFrame(WlServerDisplay display, ICompositorEventLoop loop, SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(output);
        _display = display;
        _loop = loop;
        _output = output;
    }

    /// <summary>
    /// Gets or sets the dispatch time above which a tick raises <see cref="DispatchOverran"/>. The default is 8 ms.
    /// </summary>
    public TimeSpan DispatchWarning { get; set; } = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// Gets or sets the dispatch time above which a tick raises <see cref="DispatchExceededLimit"/>. The default is 200 ms.
    /// </summary>
    public TimeSpan DispatchLimit { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Occurs when the client dispatch of a tick takes more than <see cref="DispatchWarning"/> but not more than <see cref="DispatchLimit"/>.
    /// The argument is the dispatch time. No client is disconnected.
    /// </summary>
    public event Action<TimeSpan>? DispatchOverran;

    /// <summary>
    /// Occurs when the client dispatch of a tick takes more than <see cref="DispatchLimit"/>.
    /// The argument is the dispatch time. No client is disconnected.
    /// </summary>
    public event Action<TimeSpan>? DispatchExceededLimit;

    /// <summary>
    /// Gets a value indicating whether the scene output has damage or other work for the next tick.
    /// </summary>
    public bool NeedsRepaint => _output.NeedsRepaint;

    /// <summary>
    /// Gets the number of ticks that composited a frame into the target.
    /// </summary>
    public long Composited { get; private set; }

    /// <summary>
    /// Occurs in each tick after the client dispatch and before the frame clock begins the frame.
    /// The consumer moves windows and applies input here.
    /// </summary>
    public event Action<FrameTick>? BeforeDispatch;

    /// <summary>
    /// Occurs on <see cref="RequestWakeup"/>. The consumer asks its host toolkit for a tick here.
    /// </summary>
    public event Action? WakeupRequested;

    /// <summary>
    /// Gets or sets the frame clock that each tick begins a frame on. If <see langword="null"/>, no frame clock is driven.
    /// </summary>
    public IFrameClock? Frames { get; set; }

    /// <summary>
    /// Gets or sets the clock that animations read. Each tick begins a clock frame, and the ticks of <see cref="BeforeDispatch"/> and of the scene output have animation time. If <see langword="null"/>, the ticks have monotonic time.
    /// </summary>
    public IAnimationClock? AnimationClock
    {
        get => _output.AnimationClock;
        set => _output.AnimationClock = value;
    }

    /// <summary>
    /// Raises <see cref="WakeupRequested"/>.
    /// </summary>
    public void RequestWakeup() => WakeupRequested?.Invoke();

    /// <summary>
    /// Runs one tick: dispatches the clients, raises <see cref="BeforeDispatch"/>, begins the frame on <see cref="Frames"/>, composites, ends the frame on <see cref="Frames"/> when it composited, runs idle callbacks and flushes the clients.
    /// Direct scanout and plane offload are always off.
    /// </summary>
    /// <param name="renderer">The renderer that composites the frame.</param>
    /// <param name="target">The host buffer to composite into.</param>
    /// <param name="age">The buffer age. Zero means that the contents are unknown and the whole output is drawn.</param>
    /// <param name="options">The commit options.</param>
    /// <returns><see langword="true"/> when the tick composited a frame into <paramref name="target"/>.</returns>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
    public bool Tick(IRenderer renderer, IBuffer target, int age, in SceneCommitOptions options = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(target);

        var mode = _output.Output.CurrentMode;
        var clock = _output.AnimationClock;
        clock?.BeginFrame();
        var tick = new FrameTick(
            clock?.FromMonotonic(options.TargetPresentNanos) ?? options.TargetPresentNanos,
            mode.RefreshIntervalNanoseconds);

        HostedDispatch.Run(_loop, DispatchWarning, DispatchLimit, DispatchOverran, DispatchExceededLimit);

        BeforeDispatch?.Invoke(tick);
        Frames?.BeginFrame(_output.Output, options.TargetPresentNanos);

        _state.Clear();
        var hosted = options with { AllowDirectScanout = false, AllowPlaneOffload = false };
        var composited = _output.Commit(renderer, target, age, _state, hosted);
        if (composited)
        {
            Composited++;
            Frames?.EndFrame(_output.Output, 0);
        }

        _loop.DispatchIdle();
        _display.FlushClients();
        return composited;
    }

    /// <summary>
    /// Releases the output state. The scene output, the loop and the display stay with the caller.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _state.Dispose();
    }
}
