namespace Basin.Hypr;

/// <summary>
/// Supplies the protocol pack for the Hyprland protocols and the gamescope action-binding protocol.
/// </summary>
public static class HyprPack
{
    /// <summary>
    /// Gets a new pack that holds the surface, focus-grab, lock-notify, toplevel-mapping, toplevel-export, CTM, global-shortcuts and action-binding modules.
    /// </summary>
    public static ProtocolPack Default => new(
    [
        new HyprlandSurfaceModule(),
        new HyprlandFocusGrabModule(),
        new HyprlandLockNotifyModule(),
        new HyprlandToplevelMappingModule(),
        new HyprlandToplevelExportModule(),
        new HyprlandCtmModule(),
        new HyprlandGlobalShortcutsModule(),
        new GamescopeActionBindingModule(),
    ]);
}
