namespace Basin;

public interface IProtocolModule
{
    string WireInterface { get; }

    int Version { get; }

    IReadOnlyList<Type> Capabilities => [];

    IReadOnlyList<Type> Drivers => [];

    void SeedDefaults(BasinServices services)
    {
    }

    bool ShouldInstall(BasinServices services) => true;

    IDisposable Install(BasinServices services);
}
