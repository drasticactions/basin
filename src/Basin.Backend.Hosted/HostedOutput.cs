namespace Basin.Backend.Hosted;

/// <summary>
/// An output whose size, scale and frame timing come from a host toolkit window.
/// </summary>
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

    /// <summary>
    /// Gets the buffer of the last commit that set one. The output holds a lock on it until the next buffer or destruction.
    /// </summary>
    public IBuffer? LastTarget => _presented.Buffer;

    /// <summary>
    /// Occurs when <see cref="RequestFrame"/> is called on an enabled output. The consumer asks its host for a render tick here.
    /// </summary>
    public event Action? FrameRequested;

    /// <summary>
    /// Changes the mode size and the scale and keeps the current transform.
    /// </summary>
    /// <param name="width">The new width in physical pixels.</param>
    /// <param name="height">The new height in physical pixels.</param>
    /// <param name="scale">The new scale.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is zero or negative.</exception>
    public void Resize(int width, int height, double scale) => Reconfigure(width, height, scale, Transform);

    /// <summary>
    /// Changes the mode size, the scale and the transform in one commit. The refresh rate does not change.
    /// </summary>
    /// <param name="width">The new width in physical pixels.</param>
    /// <param name="height">The new height in physical pixels.</param>
    /// <param name="scale">The new scale.</param>
    /// <param name="transform">The new transform.</param>
    /// <returns><see langword="true"/> when the commit succeeds or nothing changes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/>, <paramref name="height"/> or <paramref name="scale"/> is zero or negative.</exception>
    public bool Reconfigure(int width, int height, double scale, OutputTransform transform)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        if (CurrentMode.Width == width && CurrentMode.Height == height &&
            Math.Abs(Scale - scale) < double.Epsilon && Transform == transform)
        {
            return true;
        }

        using var state = new OutputState();
        state.SetMode(new OutputMode(width, height, CurrentMode.RefreshMilliHz));
        state.SetScale(scale);
        state.SetTransform(transform);
        return Commit(state);
    }

    /// <summary>
    /// Changes the refresh rate of the current mode.
    /// </summary>
    /// <param name="refreshMilliHz">The new refresh rate in millihertz.</param>
    /// <returns><see langword="true"/> when the commit succeeds or the rate does not change.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="refreshMilliHz"/> is zero or negative.</exception>
    public bool SetRefresh(int refreshMilliHz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refreshMilliHz);
        if (CurrentMode.RefreshMilliHz == refreshMilliHz)
        {
            return true;
        }

        using var state = new OutputState();
        state.SetMode(CurrentMode with { RefreshMilliHz = refreshMilliHz });
        return Commit(state);
    }

    /// <summary>
    /// Raises <see cref="FrameRequested"/> when the output is enabled. It does nothing on a disabled output.
    /// </summary>
    public override void RequestFrame()
    {
        if (Enabled)
        {
            FrameRequested?.Invoke();
        }
    }

    /// <summary>
    /// Raises the frame event of the output. The host calls it when it is ready for the next frame.
    /// </summary>
    public void NotifyFrame() => EmitFrame();

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

        return true;
    }

    /// <inheritdoc/>
    protected override void OnDestroy() => _presented.Dispose();
}
