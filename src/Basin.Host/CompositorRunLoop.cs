namespace Basin.Host;

/// <summary>
/// Runs the compositor's main loop until a stop, a signal or a frame count ends it. At the end it leaves the last frame lit when the host's handoff policy retains on <see cref="Stopping"/>.
/// </summary>
public sealed class CompositorRunLoop
{
    private readonly BasinHost _host;
    private OutputDriver? _outputs;
    private bool _running = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositorRunLoop"/> class.
    /// </summary>
    /// <param name="host">The host whose loop runs.</param>
    /// <param name="outputs">The output driver that counts frames and retains the last frame, or <see langword="null"/> to set it later through <see cref="Outputs"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public CompositorRunLoop(BasinHost host, OutputDriver? outputs = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _outputs = outputs;
    }

    /// <summary>
    /// Gets or sets the number of rendered frames after which the loop ends, or 0 to run until stopped.
    /// </summary>
    public long Frames { get; set; }

    /// <summary>
    /// Gets or sets the longest time in milliseconds that one dispatch waits for events.
    /// </summary>
    public int DispatchTimeoutMillis { get; set; } = 16;

    /// <summary>
    /// Gets or sets a value indicating whether the nested backend flushes its parent connection before each dispatch instead of after it.
    /// </summary>
    public bool FlushParentFirst { get; set; }

    /// <summary>
    /// Gets or sets the function that counts rendered frames for <see cref="Frames"/>, or <see langword="null"/> to use <see cref="OutputDriver.PrimaryRendered"/>.
    /// </summary>
    public Func<long>? RenderedFrames { get; set; }

    /// <summary>
    /// Occurs before each dispatch, also during the retain sequence.
    /// </summary>
    public event Action? Iterating;

    /// <summary>
    /// Occurs after each dispatch, also during the retain sequence.
    /// </summary>
    public event Action? Iterated;

    /// <summary>
    /// Gets a value indicating whether the loop runs. It is <see langword="true"/> before <see cref="Run"/> and <see langword="false"/> after <see cref="Stop"/>.
    /// </summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Gets or sets the output driver that counts frames and retains the last frame.
    /// </summary>
    public OutputDriver? Outputs
    {
        get => _outputs;
        set => _outputs = value;
    }

    /// <summary>
    /// Gets or sets the animation that runs before the last frame is retained, or <see langword="null"/> for none.
    /// </summary>
    public IHandoffOutro? Outro { get; set; }

    /// <summary>
    /// Gets or sets the longest time that the retain sequence can take. When it runs out, the frame that is lit stays lit. The default is 1 second.
    /// </summary>
    public TimeSpan OutroBudget { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the kind of stop. The program sets it before it calls <see cref="Stop"/>. The default is <see cref="HandoffStop.Orderly"/>.
    /// </summary>
    public HandoffStop Stopping { get; set; } = HandoffStop.Orderly;

    /// <summary>
    /// Gets the number of rendered frames from <see cref="RenderedFrames"/> or the first primary output, or 0 when neither exists.
    /// </summary>
    public long Rendered => RenderedFrames?.Invoke() ?? _outputs?.PrimaryRendered ?? 0;

    /// <summary>
    /// Ends the loop after the current dispatch. It is safe to call from a callback on the loop.
    /// </summary>
    public void Stop() => _running = false;

    /// <summary>
    /// Dispatches the loop until <see cref="Stop"/>, <c>SIGINT</c>, <c>SIGTERM</c> or <see cref="Frames"/> ends it. Then it runs the retain sequence when the handoff policy retains on <see cref="Stopping"/>.
    /// </summary>
    /// <returns>The number of rendered frames.</returns>
    public long Run()
    {
        var interrupt = _host.Loop.AddSignal(Signal.Interrupt, _ => Stop());
        var terminate = _host.Loop.AddSignal(Signal.Terminate, _ => Stop());
        try
        {
            while (_running && (Frames == 0 || Rendered < Frames))
            {
                Iterating?.Invoke();
                if (FlushParentFirst)
                {
                    _host.Parent?.Flush();
                }

                _host.Loop.Dispatch(DispatchTimeoutMillis);
                Iterated?.Invoke();
                if (!FlushParentFirst)
                {
                    _host.Parent?.Flush();
                }
            }
        }
        finally
        {
            interrupt.Remove();
            terminate.Remove();
        }

        Finish();
        return Rendered;
    }

    private void Finish()
    {
        if (_outputs is null || !_host.Handoff.RetainsOn(Stopping))
        {
            return;
        }

        var finished = false;
        _outputs.RetainAndStop(Outro, OutroBudget, () => finished = true);
        while (!finished)
        {
            Iterating?.Invoke();
            _host.Loop.Dispatch(5);
            Iterated?.Invoke();
            _host.Parent?.Flush();
        }
    }
}
