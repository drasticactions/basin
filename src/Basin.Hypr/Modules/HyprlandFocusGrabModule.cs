using Basin.Desktop;

namespace Basin.Hypr;

/// <summary>
/// Installs <c>hyprland_focus_grab_manager_v1</c> over the compositor seat.
/// </summary>
public sealed class HyprlandFocusGrabModule : DesktopModule<HyprlandFocusGrabManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "hyprland_focus_grab_manager_v1";

    /// <inheritdoc/>
    public override int Version => HyprlandFocusGrabManager.Version;

    /// <summary>
    /// Gets the services that the module calls. The module needs a <see cref="Basin.Seat.Seat"/> to grab.
    /// </summary>
    public override IReadOnlyList<Type> Drivers => [typeof(Basin.Seat.Seat)];

    /// <summary>
    /// Creates the manager over the display, the <see cref="CompositorGlobal"/> and the seat.
    /// </summary>
    /// <param name="services">The registry to read the services from.</param>
    /// <returns>The new manager.</returns>
    protected override HyprlandFocusGrabManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<Basin.Seat.Seat>());
}
