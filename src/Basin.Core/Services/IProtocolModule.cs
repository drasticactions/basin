namespace Basin;

/// <summary>
/// Installs one Wayland global through <see cref="BasinServices"/>. A module maps a missing capability to the negative outcome of its protocol, never to a protocol error.
/// </summary>
public interface IProtocolModule
{
    /// <summary>
    /// Gets the wire name of the global, for example <c>wl_compositor</c>. It is the key for <see cref="BasinServices.Without"/>.
    /// </summary>
    string WireInterface { get; }

    /// <summary>
    /// Gets the highest version of the global that the module advertises.
    /// </summary>
    int Version { get; }

    /// <summary>
    /// Gets the capability types that the module consumes. A missing one makes the protocol degrade, and <see cref="BasinServices.UnresolvedCapabilities"/> lists it.
    /// </summary>
    IReadOnlyList<Type> Capabilities => [];

    /// <summary>
    /// Gets the service types that the module must be able to call. <see cref="BasinServices.Freeze"/> throws when one is not registered.
    /// </summary>
    IReadOnlyList<Type> Drivers => [];

    /// <summary>
    /// Registers default implementations with <see cref="BasinServices.UseDefault{T}(T)"/>, so that a registration from the consumer wins. It runs during <see cref="BasinServices.Freeze"/>, before any install.
    /// </summary>
    /// <param name="services">The registry to seed.</param>
    void SeedDefaults(BasinServices services)
    {
    }

    /// <summary>
    /// Decides whether the global is advertised at all. A module returns false only when its protocol has no negative outcome to report.
    /// </summary>
    /// <param name="services">The registry after all defaults are seeded.</param>
    /// <returns><see langword="false"/> to skip the install.</returns>
    bool ShouldInstall(BasinServices services) => true;

    /// <summary>
    /// Creates the global. It runs once, during <see cref="BasinServices.Freeze"/>.
    /// </summary>
    /// <param name="services">The registry to read capabilities from and to add the global to.</param>
    /// <returns>The handle that destroys the global when the registry is disposed.</returns>
    IDisposable Install(BasinServices services);
}
