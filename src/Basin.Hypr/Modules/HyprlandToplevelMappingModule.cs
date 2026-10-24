using Basin.Capabilities;
using Basin.Desktop;

namespace Basin.Hypr;

/// <summary>
/// Installs <c>hyprland_toplevel_mapping_manager_v1</c> over the <see cref="IToplevelModel"/>.
/// </summary>
public sealed class HyprlandToplevelMappingModule : DesktopModule<HyprlandToplevelMappingManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "hyprland_toplevel_mapping_manager_v1";

    /// <inheritdoc/>
    public override int Version => HyprlandToplevelMappingManager.Version;

    /// <summary>
    /// Gets the capabilities that the module consumes. Without <see cref="IToplevelModel"/>, every mapping request fails.
    /// </summary>
    public override IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel)];

    /// <summary>
    /// Creates the manager over the display and the optional <see cref="IToplevelModel"/>.
    /// </summary>
    /// <param name="services">The registry to read the services from.</param>
    /// <returns>The new manager.</returns>
    protected override HyprlandToplevelMappingManager Create(BasinServices services) =>
        new(services.Display, services.Find<IToplevelModel>());
}
