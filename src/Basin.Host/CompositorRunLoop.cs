namespace Basin.Host;

public sealed class CompositorRunLoop
{
    private readonly BasinHost _host;
    private OutputDriver? _outputs;
    private bool _running = true;

    public CompositorRunLoop(BasinHost host, OutputDriver? outputs = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _outputs = outputs;
    }

    public long Frames { get; set; }

    public int DispatchTimeoutMillis { get; set; } = 16;

    public bool FlushParentFirst { get; set; }

    public Func<long>? RenderedFrames { get; set; }

    public event Action? Iterating;

    public event Action? Iterated;

    public bool IsRunning => _running;

    public OutputDriver? Outputs
    {
        get => _outputs;
        set => _outputs = value;
    }

    public IHandoffOutro? Outro { get; set; }

    public TimeSpan OutroBudget { get; set; } = TimeSpan.FromSeconds(1);

    public HandoffStop Stopping { get; set; } = HandoffStop.Orderly;

    public bool Retained { get; private set; }

    public long Rendered => RenderedFrames?.Invoke() ?? _outputs?.PrimaryRendered ?? 0;

    public void Stop() => _running = false;

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

        Retained = _host.Drm is not null;
    }
}
