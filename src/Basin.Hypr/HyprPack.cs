namespace Basin.Hypr;

public static class HyprPack
{
    public static ProtocolPack Default => new(
    [
        new HyprlandSurfaceModule(),
        new HyprlandFocusGrabModule(),
        new HyprlandLockNotifyModule(),
        new HyprlandToplevelMappingModule(),
        new HyprlandToplevelExportModule(),
        new HyprlandCtmModule(),
        new HyprlandGlobalShortcutsModule(),
    ]);
}
