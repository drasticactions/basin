using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;

namespace Basin.Hypr;

/// <summary>
/// Installs <c>hyprland_surface_manager_v1</c> over the <see cref="ISurfaceAppearance"/> of the compositor.
/// </summary>
public sealed class HyprlandSurfaceModule : DesktopModule<HyprlandSurfaceManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "hyprland_surface_manager_v1";

    /// <inheritdoc/>
    public override int Version => HyprlandSurfaceManager.Version;

    /// <summary>
    /// Gets the capabilities that the module consumes. The module reads <see cref="ISurfaceAppearance"/>.
    /// </summary>
    public override IReadOnlyList<Type> Capabilities => [typeof(ISurfaceAppearance)];

    /// <summary>
    /// Registers a <see cref="DefaultSurfaceAppearance"/> as the default, so that an appearance store from the consumer wins.
    /// </summary>
    /// <param name="services">The registry to seed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ISurfaceAppearance>(new DefaultSurfaceAppearance());
    }

    /// <summary>
    /// Creates the manager over the display, the <see cref="CompositorGlobal"/> and the <see cref="ISurfaceAppearance"/>.
    /// </summary>
    /// <param name="services">The registry to read the services from.</param>
    /// <returns>The new manager.</returns>
    protected override HyprlandSurfaceManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<ISurfaceAppearance>());
}
