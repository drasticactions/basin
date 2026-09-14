using Basin.Desktop;
using Basin.Eis;
using static Basin.Hypr.InputCapture.InputCaptureLog;

namespace Basin.Hypr.InputCapture;

public sealed class HyprlandInputCaptureModule : DesktopModule<HyprlandInputCaptureManager>
{
    public override string WireInterface => "hyprland_input_capture_manager_v1";

    public override int Version => HyprlandInputCaptureManager.Version;

    public override IReadOnlyList<Type> Drivers => [typeof(Basin.Seat.Seat)];

    public override bool ShouldInstall(BasinServices services)
    {
        if (EisLibrary.IsAvailable(out var whyNot))
        {
            return true;
        }

        Log.Info($"{WireInterface} not advertised: {whyNot}");
        return false;
    }

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
