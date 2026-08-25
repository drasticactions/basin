namespace Basin.Host;

public sealed class CompositorRunLoop
{
    private readonly BasinHost _host;
    private readonly OutputDriver? _outputs;
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

        return Rendered;
    }
}
