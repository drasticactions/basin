namespace Basin.Hypr.InputCapture;

/// <summary>
/// Supplies the protocol pack for Hyprland input capture.
/// </summary>
public static class InputCapturePack
{
    /// <summary>
    /// Gets a new pack that holds one <see cref="HyprlandInputCaptureModule"/>.
    /// </summary>
    public static ProtocolPack Default => new([new HyprlandInputCaptureModule()]);
}
