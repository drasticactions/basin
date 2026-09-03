using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public abstract class DesktopModule<T> : IProtocolModule
    where T : class, IDisposable
{
    public abstract string WireInterface { get; }

    public abstract int Version { get; }

    public virtual IReadOnlyList<Type> Capabilities => [];

    public virtual IReadOnlyList<Type> Drivers => [];

    public virtual void SeedDefaults(BasinServices services)
    {
    }

    public virtual bool ShouldInstall(BasinServices services) => true;

    public T? Manager { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = Create(services);

        services.Use(Manager);
        return Manager;
    }

    protected abstract T Create(BasinServices services);
}
