namespace Basin.Capabilities.Defaults;

public sealed class LayoutOutputConfiguration : IOutputConfiguration
{
    private const OutputConfigurationFeatures DrivableMask =
        OutputConfigurationFeatures.Overscan |
        OutputConfigurationFeatures.Vrr |
        OutputConfigurationFeatures.RgbRange |
        OutputConfigurationFeatures.MaxBitsPerColor |
        OutputConfigurationFeatures.CustomModes |
        OutputConfigurationFeatures.Sharpness |
        OutputConfigurationFeatures.AbmLevel;

    private readonly OutputLayout _layout;
    private readonly Dictionary<IOutput, Parked> _parked = [];
    private readonly Dictionary<IOutput, OutputConfigurationEntry> _committed = [];
    private readonly Dictionary<IOutput, Action> _recorded = [];

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
            if (entry.Enabled && !Replicating(entry))
            {
                Expose(entry);
            }
            else
            {
                Park(entry.Output);
            }
        }

        foreach (var entry in entries)
        {
            Record(entry);
        }

        Applied?.Invoke(entries);
        return true;
    }

    public OutputConfigurationFeatures Supported(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Features & DrivableMask;
    }

    public bool TryRead(IOutput output, out OutputConfigurationEntry state)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _committed.TryGetValue(output, out state);
    }

    private void Record(in OutputConfigurationEntry entry)
    {
        var output = entry.Output;
        var current = _committed.TryGetValue(output, out var existing)
            ? existing
            : new OutputConfigurationEntry { Output = output, Enabled = entry.Enabled };
        current = current with { Enabled = entry.Enabled };
        if (entry.Overscan is { } overscan)
        {
            current = current with { Overscan = overscan };
        }

        if (entry.RgbRange is { } rgbRange)
        {
            current = current with { RgbRange = rgbRange };
        }

        if (entry.MaxBitsPerColor is { } maxBpc)
        {
            current = current with { MaxBitsPerColor = maxBpc };
        }

        if (entry.VrrPolicy is { } vrrPolicy)
        {
            current = current with { VrrPolicy = vrrPolicy };
        }

        if (entry.CustomModes is { } customModes)
        {
            current = current with { CustomModes = customModes };
        }

        if (entry.ReplicationSourceUuid is { } replication)
        {
            current = current with { ReplicationSourceUuid = replication };
        }

        if (entry.Sharpness is { } sharpness)
        {
            current = current with { Sharpness = sharpness };
        }

        if (entry.AbmLevel is { } abmLevel)
        {
            current = current with { AbmLevel = abmLevel };
        }

        _committed[output] = current;
        if (!_recorded.ContainsKey(output))
        {
            void OnDestroyed()
            {
                _committed.Remove(output);
                _recorded.Remove(output);
            }

            _recorded[output] = OnDestroyed;
            output.Destroyed += OnDestroyed;
        }
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

    private bool Replicating(in OutputConfigurationEntry entry)
    {
        var source = entry.ReplicationSourceUuid ??
            (_committed.TryGetValue(entry.Output, out var recorded) ? recorded.ReplicationSourceUuid : null);
        return source is { Length: > 0 };
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

        if (entry.VrrPolicy is { } vrrPolicy)
        {
            state.SetAdaptiveSync(vrrPolicy == OutputVrrPolicy.Always);
        }

        if (entry.Overscan is { } overscan)
        {
            state.SetOverscan(overscan);
        }

        if (entry.RgbRange is { } rgbRange)
        {
            state.SetRgbRange(rgbRange);
        }

        if (entry.MaxBitsPerColor is { } maxBpc)
        {
            state.SetMaxBitsPerColor(maxBpc);
        }

        if (entry.CustomModes is { } customModes)
        {
            state.SetCustomModes(customModes);
        }

        if (entry.Sharpness is { } sharpness)
        {
            state.SetSharpness(sharpness);
        }

        if (entry.AbmLevel is { } abmLevel)
        {
            state.SetAbmLevel(abmLevel);
        }
    }
}
