namespace Basin.Capabilities;

public interface IOutputConfiguration
{
    bool Test(IReadOnlyList<OutputConfigurationEntry> entries);

    bool Apply(IReadOnlyList<OutputConfigurationEntry> entries);

    event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

    OutputConfigurationFeatures Supported(IOutput output) => OutputConfigurationFeatures.None;

    bool TryRead(IOutput output, out OutputConfigurationEntry state)
    {
        state = default;
        return false;
    }

    string? LastFailureReason => null;
}
