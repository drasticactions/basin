namespace Basin.Capabilities;

public interface IOutputConfiguration
{
    bool Test(IReadOnlyList<OutputConfigurationEntry> entries);

    bool Apply(IReadOnlyList<OutputConfigurationEntry> entries);

    event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;
}
