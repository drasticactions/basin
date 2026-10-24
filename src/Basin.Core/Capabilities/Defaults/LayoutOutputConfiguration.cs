using static Basin.Diagnostics.CoreLog;

namespace Basin.Capabilities.Defaults;

/// <summary>
/// Applies output configurations through output commits and the output layout. A disabled output leaves the layout and returns to its last position when it is enabled again.
/// </summary>
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

    private readonly record struct Snapshot(
        bool Enabled,
        OutputMode Mode,
        double Scale,
        OutputTransform Transform,
        bool AdaptiveSync,
        bool InLayout,
        Point Position,
        Point? ParkedAt);

    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutOutputConfiguration"/> class.
    /// </summary>
    /// <param name="layout">The layout that holds the enabled outputs.</param>
    public LayoutOutputConfiguration(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <inheritdoc/>
    public event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

    /// <summary>
    /// Gets or sets the orientation sensor that drives auto-rotation of internal panels, or <see langword="null"/> for none. Setting it applies the rotation policy again.
    /// </summary>
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

    /// <summary>
    /// Gets or sets a value indicating whether the device is in tablet mode. Outputs with the <see cref="OutputAutoRotatePolicy.InTabletMode"/> policy rotate only in tablet mode.
    /// </summary>
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

    /// <summary>
    /// Detaches from the orientation sensor.
    /// </summary>
    public void Dispose()
    {
        if (_orientation is { } sensor)
        {
            sensor.Changed -= Reevaluate;
            _orientation = null;
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool Apply(IReadOnlyList<OutputConfigurationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (!Test(entries))
        {
            return false;
        }

        var snapshots = new Snapshot[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            snapshots[i] = Capture(entries[i].Output);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            using var state = new OutputState();
            Fill(state, entry);
            if (!entry.Output.Commit(state))
            {
                Revert(entries, snapshots, i);
                return false;
            }

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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    private Snapshot Capture(IOutput output)
    {
        var inLayout = _layout.Contains(output);
        var box = inLayout ? _layout.BoxOf(output) : default;
        Point? parkedAt = _parked.TryGetValue(output, out var parked) ? parked.Position : null;
        return new Snapshot(
            output.Enabled,
            output.CurrentMode,
            output.Scale,
            output.Transform,
            output.AdaptiveSync,
            inLayout,
            new Point(box.X, box.Y),
            parkedAt);
    }

    private void Revert(IReadOnlyList<OutputConfigurationEntry> entries, Snapshot[] snapshots, int failed)
    {
        for (var i = failed - 1; i >= 0; i--)
        {
            var output = entries[i].Output;
            var snapshot = snapshots[i];
            using var state = new OutputState();
            state.SetEnabled(snapshot.Enabled);
            if (snapshot.Enabled)
            {
                state.SetMode(snapshot.Mode).SetScale(snapshot.Scale).SetTransform(snapshot.Transform)
                    .SetAdaptiveSync(snapshot.AdaptiveSync);
            }

            if (!output.Commit(state))
            {
                Log.Warn($"{output.Name}: restore failed after a refused configuration");
            }

            RestorePlacement(output, snapshot);
        }
    }

    private void RestorePlacement(IOutput output, in Snapshot snapshot)
    {
        if (snapshot.InLayout)
        {
            Unpark(output);
            if (_layout.Contains(output))
            {
                _layout.Move(output, snapshot.Position.X, snapshot.Position.Y);
            }
            else
            {
                _layout.Add(output, snapshot.Position.X, snapshot.Position.Y);
            }

            return;
        }

        if (_layout.Contains(output))
        {
            _layout.Remove(output);
        }

        if (snapshot.ParkedAt is { } parkedAt && !_parked.ContainsKey(output))
        {
            void OnDestroyed() => Unpark(output);
            _parked[output] = new Parked(parkedAt, OnDestroyed);
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
