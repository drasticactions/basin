namespace Basin.Portal;

/// <summary>
/// The portal modules as a <see cref="ProtocolPack"/>.
/// </summary>
public static class PortalPack
{
    /// <summary>
    /// Gets a new pack with all seven portal modules. Each module installs only when its services are present.
    /// </summary>
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
