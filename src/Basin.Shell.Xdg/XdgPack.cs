using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public static class XdgPack
{
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
