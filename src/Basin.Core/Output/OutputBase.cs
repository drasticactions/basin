using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin;

public abstract class OutputBase : IOutput
{
    private bool _destroyed;

    protected OutputBase(string name)
    {
        Name = name;
        BasinCounters.Track();
    }

    public string Name { get; }

    public string Description { get; protected set; } = string.Empty;

    public string Make { get; protected set; } = string.Empty;

    public string Model { get; protected set; } = string.Empty;

    public string Serial { get; protected set; } = string.Empty;

    public (int Width, int Height) PhysicalSize { get; protected set; }

    public OutputMode CurrentMode { get; private set; }

    public bool Enabled { get; private set; }

    public double Scale { get; private set; } = 1;

    public OutputTransform Transform { get; private set; }

    public bool AdaptiveSync { get; private set; }

    public event Action? Frame;

    public event Action<OutputStateFields>? Committed;

    public event Action? Destroyed;

    public bool TestCommit(OutputState state)
    {
        if (_destroyed)
        {
            return false;
        }

        if (!SupportsLayers)
        {
            RejectAllLayers(state);
        }

        if ((state.Fields & OutputStateFields.AdaptiveSync) != 0 && state.AdaptiveSync && !SupportsAdaptiveSync)
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.InFence) != 0 && state.InFenceFd >= 0 && !SupportsInFence)
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.RgbRange) != 0 &&
            state.RgbRange != OutputRgbRange.Automatic && !SupportsRgbRange)
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.MaxBitsPerColor) != 0 &&
            state.MaxBitsPerColor != 0 && !SupportsMaxBitsPerColor)
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.Overscan) != 0 && state.Overscan != 0 && !SupportsOverscan)
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.CustomModes) != 0 &&
            state.CustomModes is { Count: > 0 } && !SupportsCustomModes)
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.Sharpness) != 0 && state.Sharpness != 0 && !SupportsSharpness)
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.AbmLevel) != 0 && state.AbmLevel != 0 && !SupportsAbmLevel)
        {
            return false;
        }

        return TestCommitCore(state);
    }

    public bool Commit(OutputState state)
    {
        if (!TestCommit(state))
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.Enabled) != 0)
        {
            Enabled = state.Enabled;
        }

        if ((state.Fields & OutputStateFields.Mode) != 0)
        {
            CurrentMode = state.Mode;
        }

        if ((state.Fields & OutputStateFields.Scale) != 0)
        {
            Scale = state.Scale;
        }

        if ((state.Fields & OutputStateFields.Transform) != 0)
        {
            Transform = state.Transform;
        }

        if ((state.Fields & OutputStateFields.AdaptiveSync) != 0)
        {
            AdaptiveSync = state.AdaptiveSync;
        }

        if (!CommitCore(state))
        {
            return false;
        }

        Committed?.Invoke(state.Fields);
        return true;
    }

    public void Destroy()
    {
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        OnDestroy();
        Destroyed?.Invoke();
        BasinCounters.Untrack();
    }

    public virtual void RequestFrame()
    {
        if (Enabled)
        {
            EmitFrame();
        }
    }

    protected virtual bool SupportsLayers => false;

    protected virtual bool SupportsAdaptiveSync => false;

    protected virtual bool SupportsRgbRange => false;

    protected virtual bool SupportsMaxBitsPerColor => false;

    protected virtual bool SupportsOverscan => false;

    protected virtual bool SupportsCustomModes => false;

    protected virtual bool SupportsSharpness => false;

    protected virtual bool SupportsAbmLevel => false;

    public virtual OutputConfigurationFeatures Features => OutputConfigurationFeatures.None;

    public virtual OutputColorimetry? Colorimetry => null;

    public virtual ReadOnlyMemory<byte> EdidBytes => default;

    public virtual bool SupportsInFence => false;

    public virtual bool CanScanout(DrmFormat format, ulong modifier, bool overlay) => true;

    private static void RejectAllLayers(OutputState state)
    {
        if ((state.Fields & OutputStateFields.Layers) == 0 || state.Layers is null)
        {
            return;
        }

        var layers = state.Layers;
        for (var i = 0; i < layers.Count; i++)
        {
            layers[i].Accepted = false;
        }
    }

    protected abstract bool TestCommitCore(OutputState state);

    protected abstract bool CommitCore(OutputState state);

    protected virtual void OnDestroy()
    {
    }

    protected void EmitFrame()
    {
        if (!_destroyed)
        {
            Frame?.Invoke();
        }
    }
}
