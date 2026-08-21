using Basin.Capabilities;
using Drm;

namespace Basin.Backend.Drm;

public sealed class DrmOutputPower : IOutputPower
{
    private readonly Dictionary<IOutput, bool> _states = [];

    public event Action<IOutput>? PowerChanged;

    public bool IsOn(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _states.GetValueOrDefault(output, output.Enabled);
    }

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
