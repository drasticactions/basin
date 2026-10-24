using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;

namespace Basin.Hypr;

/// <summary>
/// Installs <c>hyprland_global_shortcuts_manager_v1</c> over the <see cref="IGlobalShortcuts"/> registry.
/// </summary>
public sealed class HyprlandGlobalShortcutsModule : DesktopModule<HyprlandGlobalShortcutsManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "hyprland_global_shortcuts_manager_v1";

    /// <inheritdoc/>
    public override int Version => HyprlandGlobalShortcutsManager.Version;

    /// <summary>
    /// Gets the capabilities that the module consumes. The module reads <see cref="IGlobalShortcuts"/>.
    /// </summary>
    public override IReadOnlyList<Type> Capabilities => [typeof(IGlobalShortcuts)];

    /// <summary>
    /// Registers a <see cref="DefaultGlobalShortcuts"/> as the default registry, so that a registry from the consumer wins.
    /// </summary>
    /// <param name="services">The registry to seed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<IGlobalShortcuts>(new DefaultGlobalShortcuts());
    }

    /// <summary>
    /// Creates the manager over the display and the <see cref="IGlobalShortcuts"/> registry.
    /// </summary>
    /// <param name="services">The registry to read the services from.</param>
    /// <returns>The new manager.</returns>
    protected override HyprlandGlobalShortcutsManager Create(BasinServices services) =>
        new(services.Display, services.Require<IGlobalShortcuts>());
}
