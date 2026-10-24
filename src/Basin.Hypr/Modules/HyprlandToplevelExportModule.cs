using Basin.Capabilities;
using Basin.Desktop;

namespace Basin.Hypr;

/// <summary>
/// Installs <c>hyprland_toplevel_export_manager_v1</c> over the screen capture and the toplevel model of the compositor.
/// </summary>
public sealed class HyprlandToplevelExportModule : DesktopModule<HyprlandToplevelExportManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "hyprland_toplevel_export_manager_v1";

    /// <inheritdoc/>
    public override int Version => HyprlandToplevelExportManager.Version;

    /// <summary>
    /// Gets the capabilities that the module consumes. Without <see cref="IScreenCapture"/> or <see cref="IToplevelModel"/>, every frame fails.
    /// </summary>
    public override IReadOnlyList<Type> Capabilities => [typeof(IScreenCapture), typeof(IToplevelModel)];

    /// <summary>
    /// Creates the manager. The capture, the toplevel model and the dmabuf constraints are optional.
    /// </summary>
    /// <param name="services">The registry to read the services from.</param>
    /// <returns>The new manager.</returns>
    protected override HyprlandToplevelExportManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<OutputLayout>(),
            services.Require<ClientBufferRegistry>(),
            services.Find<IScreenCapture>(),
            services.Find<IToplevelModel>(),
            services.Find<ICaptureDmabufConstraints>());
}
