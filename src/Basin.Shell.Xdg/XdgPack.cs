using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// Makes the protocol pack for xdg-shell, its extensions and layer-shell.
/// </summary>
public static class XdgPack
{
    /// <summary>
    /// Gets a new pack with xdg_wm_base, xdg-decoration, xdg-dialog, xdg-toplevel-icon, xdg-toplevel-tag, xdg-toplevel-drag and wlr-layer-shell. Each read makes new module instances. A compositor that uses the pack must register an <see cref="ILayerShellDriver"/>.
    /// </summary>
    public static ProtocolPack Default => new(
    [
        new XdgShellModule(),
        new XdgDecorationModule(),
        new XdgDialogModule(),
        new XdgToplevelIconModule(),
        new XdgToplevelTagModule(),
        new XdgToplevelDragModule(),
        new LayerShellModule(),
    ]);
}
