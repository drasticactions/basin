using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class AutoRotateOutputConfiguration : IOutputConfiguration, IDisposable
{
    private readonly IOutputConfiguration _inner;
    private readonly IOrientationSource _sensor;
    private readonly Dictionary<IOutput, OutputAutoRotatePolicy> _policies = [];
    private readonly Dictionary<IOutput, OutputTransform> _manual = [];
    private readonly Dictionary<IOutput, Action> _cleanup = [];
    private bool _tabletMode;

    public AutoRotateOutputConfiguration(IOutputConfiguration inner, IOrientationSource sensor)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(sensor);
        _inner = inner;
        _sensor = sensor;
        sensor.Changed += Reevaluate;
    }

    public event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

    public string? LastFailureReason => _inner.LastFailureReason;

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

    public void Dispose() => _sensor.Changed -= Reevaluate;

    public OutputConfigurationFeatures Supported(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var features = _inner.Supported(output);
        if (_sensor.IsAvailable && InternalConnectors.IsInternal(output))
        {
            features |= OutputConfigurationFeatures.AutoRotate;
        }

        return features;
    }

    public bool Test(IReadOnlyList<OutputConfigurationEntry> entries) => _inner.Test(entries);

    public bool Apply(IReadOnlyList<OutputConfigurationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!_inner.Apply(entries))
        {
            return false;
        }

        foreach (var entry in entries)
        {
            Record(entry);
        }

        Reevaluate();
        Applied?.Invoke(entries);
        return true;
    }

    public bool TryRead(IOutput output, out OutputConfigurationEntry state)
    {
        ArgumentNullException.ThrowIfNull(output);
        var any = _inner.TryRead(output, out state);
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

    private void Record(in OutputConfigurationEntry entry)
    {
        var output = entry.Output;
        if (entry.Transform is { } transform && !RotationActive(output))
        {
            _manual[output] = transform;
        }

        if (entry.AutoRotate is { } policy)
        {
            _policies[output] = policy;
        }

        if ((entry.Transform is not null || entry.AutoRotate is not null) && !_cleanup.ContainsKey(output))
        {
            void OnDestroyed()
            {
                _policies.Remove(output);
                _manual.Remove(output);
                _cleanup.Remove(output);
            }

            _cleanup[output] = OnDestroyed;
            output.Destroyed += OnDestroyed;
        }
    }

    private bool RotationActive(IOutput output) =>
        _sensor.IsAvailable && InternalConnectors.IsInternal(output) &&
        _policies.GetValueOrDefault(output, OutputAutoRotatePolicy.Never) switch
        {
            OutputAutoRotatePolicy.Always => true,
            OutputAutoRotatePolicy.InTabletMode => _tabletMode,
            _ => false,
        };

    private void Reevaluate()
    {
        var anyActive = false;
        foreach (var (output, _) in _policies)
        {
            if (RotationActive(output))
            {
                anyActive = true;
                if (_sensor.Orientation is { } orientation && output.Enabled && output.Transform != orientation)
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

        _sensor.SetEnabled(anyActive);
    }
}
