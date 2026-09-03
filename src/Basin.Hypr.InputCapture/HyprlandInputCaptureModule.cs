using Basin.Desktop;
using static Basin.Hypr.InputCapture.InputCaptureLog;

namespace Basin.Hypr.InputCapture;

public sealed class HyprlandInputCaptureModule : DesktopModule<HyprlandInputCaptureManager>
{
    public override string WireInterface => "hyprland_input_capture_manager_v1";

    public override int Version => HyprlandInputCaptureManager.Version;

    public override IReadOnlyList<Type> Drivers => [typeof(Basin.Seat.Seat)];

    public override bool ShouldInstall(BasinServices services)
    {
        if (InputCaptureLibrary.IsAvailable(out var whyNot))
        {
            return true;
        }

        Log.Info($"{WireInterface} not advertised: {whyNot}");
        return false;
    }

    protected override HyprlandInputCaptureManager Create(BasinServices services) =>
        new(services.Display, services.Loop, services.Require<OutputLayout>(), services.Require<Basin.Seat.Seat>());
}
