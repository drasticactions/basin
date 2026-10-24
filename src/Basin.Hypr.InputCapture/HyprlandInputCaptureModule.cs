using Basin.Desktop;
using Basin.Eis;
using static Basin.Hypr.InputCapture.InputCaptureLog;

namespace Basin.Hypr.InputCapture;

/// <summary>
/// Installs <c>hyprland_input_capture_manager_v1</c>. It shares the <see cref="InputCaptureEngine"/> registered in the services, or registers a new one.
/// </summary>
public sealed class HyprlandInputCaptureModule : DesktopModule<HyprlandInputCaptureManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "hyprland_input_capture_manager_v1";

    /// <inheritdoc/>
    public override int Version => HyprlandInputCaptureManager.Version;

    /// <summary>
    /// Gets the services that the module calls. The module needs a <see cref="Basin.Seat.Seat"/> to grab input from.
    /// </summary>
    public override IReadOnlyList<Type> Drivers => [typeof(Basin.Seat.Seat)];

    /// <summary>
    /// Skips the global when libeis cannot load, and logs the reason.
    /// </summary>
    /// <param name="services">The registry. The check does not read it.</param>
    /// <returns><see langword="true"/> when libeis is available.</returns>
    public override bool ShouldInstall(BasinServices services)
    {
        if (EisLibrary.IsAvailable(out var whyNot))
        {
            return true;
        }

        Log.Info($"{WireInterface} not advertised, {whyNot}");
        return false;
    }

    /// <summary>
    /// Creates the manager over the shared <see cref="InputCaptureEngine"/>. If no engine is registered, it creates one and registers it.
    /// </summary>
    /// <param name="services">The registry that supplies the display, loop, layout and seat.</param>
    /// <returns>The new manager.</returns>
    protected override HyprlandInputCaptureManager Create(BasinServices services)
    {
        if (services.Find<InputCaptureEngine>() is { } shared)
        {
            return new HyprlandInputCaptureManager(services.Display, shared);
        }

        var manager = new HyprlandInputCaptureManager(
            services.Display, services.Loop, services.Require<OutputLayout>(), services.Require<Basin.Seat.Seat>());
        services.Use(manager.Engine);
        return manager;
    }
}
