namespace Basin.Host;

internal sealed class RetainRun
{
    public RetainRun(IHandoffOutro? outro, Action done)
    {
        Outro = outro;
        Done = done;
    }

    public IHandoffOutro? Outro { get; }

    public Action Done { get; }

    public long OutroStarted { get; set; } = -1;

    public bool OutroDone { get; set; }

    public bool Finished { get; set; }

    public IEventSource? Deadline { get; set; }

    public HashSet<OutputView> Flattening { get; } = [];

    public HashSet<OutputView> AwaitingFlip { get; } = [];

    public HashSet<OutputView> Presented { get; } = [];

    public List<(IOutput Output, Action Handler)> FrameHandlers { get; } = [];
}
