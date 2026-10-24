namespace Basin;

/// <summary>
/// A reusable set of capability registrations that <see cref="BasinServices.With"/> applies in one call.
/// </summary>
public interface ICapabilityPack
{
    /// <summary>
    /// Registers the capabilities of the pack. It runs before <see cref="BasinServices.Freeze"/>.
    /// </summary>
    /// <param name="services">The registry to add to.</param>
    void Register(BasinServices services);
}
