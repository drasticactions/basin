namespace Basin.Capabilities.Defaults;

public sealed class LayoutOutputConfiguration : IOutputConfiguration, IDisposable
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
    private readonly Dictionary<IOutput, OutputAutoRotatePolicy> _policies = [];
    private readonly Dictionary<IOutput, OutputTransform> _manual = [];
    private IOrientationSource? _orientation;
    private bool _tabletMode;

    private sealed record Parked(Point Position, Action OnDestroyed);

    public LayoutOutputConfiguration(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    public event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

    public IOrientationSource? Orientation
    {
        get => _orientation;
        set
        {
            if (ReferenceEquals(_orientation, value))
            {
                return;
            }

            if (_orientation is { } previous)
            {
                previous.Changed -= Reevaluate;
            }

            _orientation = value;
            if (value is { } next)
            {
                next.Changed += Reevaluate;
            }

            Reevaluate();
        }
    }

    public bool TabletMode
    {
        get => _tabletMode;
        set
        {
            if (_tabletMode != value)
            {
                _tabletMode = value;
                Reevaluate();
            }
        }
    }

    public void Dispose()
    {
        if (_orientation is { } sensor)
        {
            sensor.Changed -= Reevaluate;
            _orientation = null;
        }
    }

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

        Reevaluate();
        Applied?.Invoke(entries);
        return true;
    }

    public OutputConfigurationFeatures Supported(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var features = output.Features & DrivableMask;
        if (_orientation is { IsAvailable: true } && InternalConnectors.IsInternal(output))
        {
            features |= OutputConfigurationFeatures.AutoRotate;
        }

        return features;
    }

    public bool TryRead(IOutput output, out OutputConfigurationEntry state)
    {
        ArgumentNullException.ThrowIfNull(output);
        var any = _committed.TryGetValue(output, out state);
        if (_policies.TryGetValue(output, out var policy))
        {
            if (!any)
            {
                state = new OutputConfigurationEntry { Output = output, Enabled = output.Enabled };
                any = true;
            }

            state = state with { AutoRotate = policy };
        }

        return any;
    }

    private bool RotationActive(IOutput output) =>
        _orientation is { IsAvailable: true } && InternalConnectors.IsInternal(output) &&
        _policies.GetValueOrDefault(output, OutputAutoRotatePolicy.Never) switch
        {
            OutputAutoRotatePolicy.Always => true,
            OutputAutoRotatePolicy.InTabletMode => _tabletMode,
            _ => false,
        };

    private void Reevaluate()
    {
        if (_orientation is not { } sensor)
        {
            return;
        }

        var anyActive = false;
        foreach (var (output, _) in _policies)
        {
            if (RotationActive(output))
            {
                anyActive = true;
                if (sensor.Orientation is { } orientation && output.Enabled && output.Transform != orientation)
                {
                    using var state = new OutputState();
                    _ = output.Commit(state.SetTransform(orientation));
                }
            }
            else
            {
                var manual = _manual.GetValueOrDefault(output, OutputTransform.Normal);
                if (output.Enabled && output.Transform != manual)
                {
                    using var state = new OutputState();
                    _ = output.Commit(state.SetTransform(manual));
                }
            }
        }

        sensor.SetEnabled(anyActive);
    }

    private void Record(in OutputConfigurationEntry entry)
    {
        var output = entry.Output;
        if (entry.Transform is { } rotated && !RotationActive(output))
        {
            _manual[output] = rotated;
        }

        if (entry.AutoRotate is { } autoRotate)
        {
            _policies[output] = autoRotate;
        }

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
                _policies.Remove(output);
                _manual.Remove(output);
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
