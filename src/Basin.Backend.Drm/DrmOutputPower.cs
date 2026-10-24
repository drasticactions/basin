using Basin.Capabilities;

namespace Basin.Backend.Drm;

/// <summary>
/// Turns outputs on and off with an output-state commit. It keeps no state, so the power state is the output's enabled state.
/// </summary>
public sealed class DrmOutputPower : IOutputPower
{
    /// <summary>
    /// Occurs after <see cref="SetOn"/> commits a new state.
    /// </summary>
    public event Action<IOutput>? PowerChanged;

    /// <summary>
    /// Gets the power state of an output.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The output's enabled state.</returns>
    public bool IsOn(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Enabled;
    }

    /// <summary>
    /// Enables or disables an output. The state is tested before it is committed.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="on"><see langword="true"/> to enable the output.</param>
    /// <returns><see langword="true"/> when the commit succeeded.</returns>
    public bool SetOn(IOutput output, bool on)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var state = new OutputState();
        state.SetEnabled(on);
        if (!output.TestCommit(state) || !output.Commit(state))
        {
            return false;
        }

        PowerChanged?.Invoke(output);
        return true;
    }
}
