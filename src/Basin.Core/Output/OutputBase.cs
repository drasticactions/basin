using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// Implements the shared commit rules of <see cref="IOutput"/> for a backend. It refuses features that the subclass does not declare, keeps the committed fields and raises the events.
/// </summary>
public abstract class OutputBase : IOutput
{
    private bool _destroyed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputBase"/> class. The output counts as a live object until <see cref="Destroy"/>.
    /// </summary>
    /// <param name="name">The stable output name.</param>
    protected OutputBase(string name)
    {
        Name = name;
        BasinCounters.Track();
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Description { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public string Make { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public string Model { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public string Serial { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public (int Width, int Height) PhysicalSize { get; protected set; }

    /// <inheritdoc/>
    public OutputMode CurrentMode { get; private set; }

    /// <inheritdoc/>
    public bool Enabled { get; private set; }

    /// <inheritdoc/>
    public double Scale { get; private set; } = 1;

    /// <inheritdoc/>
    public OutputTransform Transform { get; private set; }

    /// <inheritdoc/>
    public bool AdaptiveSync { get; private set; }

    /// <inheritdoc/>
    public virtual bool LastFlipWasAsync => false;

    /// <inheritdoc/>
    public virtual long PlaneEpoch => 0;

    /// <inheritdoc/>
    public virtual OutputTransform PanelOrientation => OutputTransform.Normal;

    /// <inheritdoc/>
    public event Action? Frame;

    /// <inheritdoc/>
    public event Action? RepaintRequested;

    /// <inheritdoc/>
    public void RequestRepaint() => RepaintRequested?.Invoke();

    /// <inheritdoc/>
    public event Action<OutputStateFields>? Committed;

    /// <inheritdoc/>
    public event Action? Destroyed;

    /// <summary>
    /// Occurs when the list of modes that the output offers changes. A subclass raises it through <see cref="RaiseModesChanged"/>.
    /// </summary>
    public event Action? ModesChanged;

    /// <summary>
    /// Raises <see cref="ModesChanged"/>.
    /// </summary>
    protected void RaiseModesChanged() => ModesChanged?.Invoke();

    /// <summary>
    /// Sets <see cref="CurrentMode"/> without a commit, for a backend whose current mode left the mode list, for example after a hotplug.
    /// </summary>
    /// <param name="mode">The mode that is now in force.</param>
    protected void ReplaceCurrentMode(OutputMode mode) => CurrentMode = mode;

    /// <summary>
    /// Tests a state without applying it. It fails on a destroyed output, and on a field that the subclass does not support, before <see cref="TestCommitCore"/> runs. Without layer support, it rejects every layer.
    /// </summary>
    /// <param name="state">The staged changes.</param>
    /// <returns><see langword="true"/> when the state can be committed.</returns>
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

    /// <summary>
    /// Tests a state, records its fields on this object and then calls <see cref="CommitCore"/>. On success it raises <see cref="Committed"/>. When <see cref="CommitCore"/> fails, it restores the fields that the state set and raises nothing.
    /// </summary>
    /// <param name="state">The staged changes. File descriptors in it are borrowed for the length of the call.</param>
    /// <returns><see langword="false"/> when the test or <see cref="CommitCore"/> failed.</returns>
    public bool Commit(OutputState state)
    {
        if (!TestCommit(state))
        {
            return false;
        }

        var fields = state.Fields;
        var oldEnabled = Enabled;
        var oldMode = CurrentMode;
        var oldScale = Scale;
        var oldTransform = Transform;
        var oldAdaptiveSync = AdaptiveSync;

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
            if ((fields & OutputStateFields.Enabled) != 0)
            {
                Enabled = oldEnabled;
            }

            if ((fields & OutputStateFields.Mode) != 0)
            {
                CurrentMode = oldMode;
            }

            if ((fields & OutputStateFields.Scale) != 0)
            {
                Scale = oldScale;
            }

            if ((fields & OutputStateFields.Transform) != 0)
            {
                Transform = oldTransform;
            }

            if ((fields & OutputStateFields.AdaptiveSync) != 0)
            {
                AdaptiveSync = oldAdaptiveSync;
            }

            return false;
        }

        Committed?.Invoke(state.Fields);
        return true;
    }

    /// <summary>
    /// Destroys the output. It calls <see cref="OnDestroy"/>, raises <see cref="Destroyed"/> and stops the live-object count. A second call does nothing.
    /// </summary>
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

    /// <summary>
    /// Asks for a <see cref="Frame"/> event. The base raises it at once when the output is enabled. A backend can override it to wait for a pending flip.
    /// </summary>
    public virtual void RequestFrame()
    {
        if (Enabled)
        {
            EmitFrame();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the backend can put layers on hardware planes. When it is <see langword="false"/>, every layer is rejected before <see cref="TestCommitCore"/> runs.
    /// </summary>
    protected virtual bool SupportsLayers => false;

    /// <summary>
    /// Gets a value indicating whether the backend can turn on variable refresh. When it is <see langword="false"/>, a commit that turns it on fails.
    /// </summary>
    protected virtual bool SupportsAdaptiveSync => false;

    /// <summary>
    /// Gets a value indicating whether the backend can set the RGB quantization range. When it is <see langword="false"/>, a commit that sets a range other than automatic fails.
    /// </summary>
    protected virtual bool SupportsRgbRange => false;

    /// <summary>
    /// Gets a value indicating whether the backend can limit the bits per color on the link. When it is <see langword="false"/>, a commit with a non-zero limit fails.
    /// </summary>
    protected virtual bool SupportsMaxBitsPerColor => false;

    /// <summary>
    /// Gets a value indicating whether the backend can set overscan. When it is <see langword="false"/>, a commit with non-zero overscan fails.
    /// </summary>
    protected virtual bool SupportsOverscan => false;

    /// <summary>
    /// Gets a value indicating whether the backend can drive custom modes. When it is <see langword="false"/>, a commit with custom modes fails.
    /// </summary>
    protected virtual bool SupportsCustomModes => false;

    /// <summary>
    /// Gets a value indicating whether the backend can set the panel sharpness. When it is <see langword="false"/>, a commit with non-zero sharpness fails.
    /// </summary>
    protected virtual bool SupportsSharpness => false;

    /// <summary>
    /// Gets a value indicating whether the backend can set the adaptive backlight level. When it is <see langword="false"/>, a commit with a non-zero level fails.
    /// </summary>
    protected virtual bool SupportsAbmLevel => false;

    /// <inheritdoc/>
    public virtual OutputClass Class => OutputClass.Desktop;

    /// <inheritdoc/>
    public virtual OutputConfigurationFeatures Features => OutputConfigurationFeatures.None;

    /// <inheritdoc/>
    public virtual OutputColorimetry? Colorimetry => null;

    /// <inheritdoc/>
    public virtual ReadOnlyMemory<byte> EdidBytes => default;

    /// <inheritdoc/>
    public virtual bool SupportsInFence => false;

    /// <inheritdoc/>
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

    /// <summary>
    /// Runs the backend's own test of a state, after the shared checks passed.
    /// </summary>
    /// <param name="state">The staged changes.</param>
    /// <returns><see langword="true"/> when the backend can apply the state.</returns>
    protected abstract bool TestCommitCore(OutputState state);

    /// <summary>
    /// Applies a state on the backend. A backend that defers presentation still returns <see langword="true"/> when the frame stays on a path to the screen.
    /// </summary>
    /// <param name="state">The staged changes. File descriptors in it are borrowed for the length of the call.</param>
    /// <returns><see langword="false"/> when the state was not applied and never will be.</returns>
    protected abstract bool CommitCore(OutputState state);

    /// <summary>
    /// Releases the backend resources of the output. <see cref="Destroy"/> calls it once, before it raises <see cref="Destroyed"/>.
    /// </summary>
    protected virtual void OnDestroy()
    {
    }

    /// <summary>
    /// Raises <see cref="Frame"/>, unless the output is destroyed.
    /// </summary>
    protected void EmitFrame()
    {
        if (!_destroyed)
        {
            Frame?.Invoke();
        }
    }
}
