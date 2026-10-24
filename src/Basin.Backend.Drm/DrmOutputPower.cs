using Basin.Capabilities;
using Drm;

namespace Basin.Backend.Drm;

/// <summary>
/// Turns outputs on and off with an output-state commit, and remembers the last state that it set.
/// </summary>
public sealed class DrmOutputPower : IOutputPower
{
    private readonly Dictionary<IOutput, bool> _states = [];

    /// <summary>
    /// Occurs after <see cref="SetOn"/> commits a new state.
    /// </summary>
    public event Action<IOutput>? PowerChanged;

    /// <summary>
    /// Gets the power state of an output.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The last state that <see cref="SetOn"/> set, or the output's enabled state when it set none.</returns>
    public bool IsOn(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _states.GetValueOrDefault(output, output.Enabled);
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

        _states[output] = on;
        PowerChanged?.Invoke(output);
        return true;
    }
}
