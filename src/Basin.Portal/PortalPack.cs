namespace Basin.Portal;

public static class PortalPack
{
    public static ProtocolPack Default => new(
    [
        new PortalScreenCastModule(),
        new PortalRemoteDesktopModule(),
        new PortalScreenshotModule(),
        new PortalInputCaptureModule(),
        new PortalClipboardModule(),
        new PortalGlobalShortcutsModule(),
        new PortalAccessModule(),
    ]);
}
