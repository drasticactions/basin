using Basin.Capabilities;
using Basin.Desktop;

namespace Basin.Hypr;

/// <summary>
/// Installs <c>hyprland_ctm_control_manager_v1</c> over the <see cref="ICtmControl"/> that the compositor registers.
/// </summary>
public sealed class HyprlandCtmModule : DesktopModule<HyprlandCtmControlManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "hyprland_ctm_control_manager_v1";

    /// <inheritdoc/>
    public override int Version => HyprlandCtmControlManager.Version;

    /// <summary>
    /// Gets the services that the module calls. The module needs an <see cref="ICtmControl"/>.
    /// </summary>
    public override IReadOnlyList<Type> Drivers => [typeof(ICtmControl)];

    /// <summary>
    /// Creates the manager over the display, the <see cref="OutputLayout"/> and the <see cref="ICtmControl"/>.
    /// </summary>
    /// <param name="services">The registry to read the services from.</param>
    /// <returns>The new manager.</returns>
    protected override HyprlandCtmControlManager Create(BasinServices services) =>
        new(services.Display, services.Require<OutputLayout>(), services.Require<ICtmControl>());
}
