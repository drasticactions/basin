using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Base for a module that installs one manager. <see cref="Install"/> creates the manager, registers it in <see cref="BasinServices"/> under its own type, and returns it as the install handle.
/// </summary>
/// <typeparam name="T">The manager type. Disposing it removes the global.</typeparam>
public abstract class DesktopModule<T> : IProtocolModule
    where T : class, IDisposable
{
    /// <inheritdoc/>
    public abstract string WireInterface { get; }

    /// <inheritdoc/>
    public abstract int Version { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<Type> Capabilities => [];

    /// <inheritdoc/>
    public virtual IReadOnlyList<Type> Drivers => [];

    /// <inheritdoc/>
    public virtual void SeedDefaults(BasinServices services)
    {
    }

    /// <inheritdoc/>
    public virtual bool ShouldInstall(BasinServices services) => true;

    /// <summary>
    /// Gets the manager that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public T? Manager { get; private set; }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = Create(services);

        services.Use(Manager);
        return Manager;
    }

    /// <summary>
    /// Creates the manager. <see cref="Install"/> calls it once, after all defaults are seeded.
    /// </summary>
    /// <param name="services">The frozen registry to read capabilities and drivers from.</param>
    /// <returns>The new manager, which the caller owns.</returns>
    protected abstract T Create(BasinServices services);
}
