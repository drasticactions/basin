namespace Basin.Backend.Hosted;

public sealed class HostedOutput : OutputBase
{
    private BufferLock _presented;

    internal HostedOutput(string name, OutputMode mode, double scale)
        : base(name)
    {
        Make = "basin";
        Model = "hosted";
        Description = $"hosted output {name}";

        using var initial = new OutputState();
        initial.SetEnabled(true).SetMode(mode);
        if (scale > 0)
        {
            initial.SetScale(scale);
        }

        Commit(initial);
    }

    public IBuffer? LastTarget => _presented.Buffer;

    public event Action? FrameRequested;

    public void Resize(int width, int height, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        if (CurrentMode.Width == width && CurrentMode.Height == height &&
            Math.Abs(Scale - scale) < double.Epsilon)
        {
            return;
        }

        using var state = new OutputState();
        state.SetMode(new OutputMode(width, height, CurrentMode.RefreshMilliHz));
        state.SetScale(scale);
        Commit(state);
    }

    public override void RequestFrame()
    {
        if (Enabled)
        {
            FrameRequested?.Invoke();
        }
    }

    public void NotifyFrame() => EmitFrame();

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

    protected override bool CommitCore(OutputState state)
    {
        if ((state.Fields & OutputStateFields.Buffer) != 0)
        {
            var presented = state.Buffer!.Lock();
            _presented.Dispose();
            _presented = presented;
        }

        return true;
    }

    protected override void OnDestroy() => _presented.Dispose();
}
