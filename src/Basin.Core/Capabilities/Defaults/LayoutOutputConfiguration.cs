namespace Basin.Capabilities.Defaults;

public sealed class LayoutOutputConfiguration : IOutputConfiguration
{
    private readonly OutputLayout _layout;
    private readonly Dictionary<IOutput, Parked> _parked = [];

    private sealed record Parked(Point Position, Action OnDestroyed);

    public LayoutOutputConfiguration(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    public event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

    public bool Test(IReadOnlyList<OutputConfigurationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            using var state = new OutputState();
            Fill(state, entry);
            if (!entry.Output.TestCommit(state))
            {
                return false;
            }
        }

        return true;
    }

    public bool Apply(IReadOnlyList<OutputConfigurationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (!Test(entries))
        {
            return false;
        }

        foreach (var entry in entries)
        {
            using var state = new OutputState();
            Fill(state, entry);
            entry.Output.Commit(state);
            if (entry.Enabled)
            {
                Expose(entry);
            }
            else
            {
                Park(entry.Output);
            }
        }

        Applied?.Invoke(entries);
        return true;
    }

    private void Expose(in OutputConfigurationEntry entry)
    {
        if (_layout.Contains(entry.Output))
        {
            if (entry.Position is { } moved)
            {
                _layout.Move(entry.Output, moved.X, moved.Y);
            }

            return;
        }

        var position = entry.Position ?? Unpark(entry.Output);
        _layout.Add(entry.Output, position.X, position.Y);
    }

    private void Park(IOutput output)
    {
        if (!_layout.Contains(output) || _parked.ContainsKey(output))
        {
            return;
        }

        var box = _layout.BoxOf(output);
        void OnDestroyed() => Unpark(output);
        _parked[output] = new Parked(new Point(box.X, box.Y), OnDestroyed);
        output.Destroyed += OnDestroyed;
        _layout.Remove(output);
    }

    private Point Unpark(IOutput output)
    {
        if (!_parked.Remove(output, out var parked))
        {
            return default;
        }

        output.Destroyed -= parked.OnDestroyed;
        return parked.Position;
    }

    private bool PoweredOff(IOutput output) => _layout.Contains(output) && !output.Enabled;

    private void Fill(OutputState state, in OutputConfigurationEntry entry)
    {
        state.SetEnabled(entry.Enabled && !PoweredOff(entry.Output));
        if (!entry.Enabled)
        {
            return;
        }

        if (entry.Mode is { } mode)
        {
            state.SetMode(mode);
        }

        if (entry.Scale is { } scale)
        {
            state.SetScale(scale);
        }

        if (entry.Transform is { } transform)
        {
            state.SetTransform(transform);
        }

        if (entry.AdaptiveSync is { } adaptiveSync)
        {
            state.SetAdaptiveSync(adaptiveSync);
        }
    }
}
