namespace Basin.AppMenu;

/// <summary>
/// Installs <c>gtk_shell1</c>, which records the menu addresses of client surfaces in <see cref="AppMenuAddresses"/>.
/// It seeds an empty <see cref="AppMenuAddresses"/> table when the consumer registers none.
/// </summary>
public sealed class GtkShellModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "gtk_shell1";

    /// <inheritdoc/>
    public int Version => GtkShellManager.Version;

    /// <summary>
    /// Gets the global that <see cref="Install"/> created, or <see langword="null"/> before the install.
    /// </summary>
    public GtkShellManager? Manager { get; private set; }

    /// <inheritdoc/>
    public void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault(new AppMenuAddresses());
    }

    /// <summary>
    /// Creates the <see cref="GtkShellManager"/> and registers it in <paramref name="services"/>. It requires a <see cref="CompositorGlobal"/>.
    /// </summary>
    /// <param name="services">The registry to read the compositor and the address table from.</param>
    /// <returns>The global, which removes itself when disposed.</returns>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new GtkShellManager(services.Display, services.Require<CompositorGlobal>(), services.Require<AppMenuAddresses>());
        services.Use(Manager);
        return Manager;
    }
}
