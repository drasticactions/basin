namespace Basin.Hypr.InputCapture;

public static class InputCapturePack
{
    public static ProtocolPack Default => new([new HyprlandInputCaptureModule()]);
}
