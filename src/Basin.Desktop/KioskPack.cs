using Basin.Shell.Xdg;

namespace Basin.Desktop;

public static class KioskPack
{
    public static ProtocolPack Default =>
        CorePack.Default.Without("wl_fixes")
        + Seat.SeatPack.For()
        + new ProtocolPack(
        [
            new XdgShellModule(),
            new XdgDecorationModule(),
            new PrimarySelectionModule(),
            new XdgOutputModule(),
            new OutputManagementModule(),
            new GammaControlModule(),
            new DrmLeaseModule(),
            new ForeignToplevelModule(),
            new ScreencopyModule(),
            new ExportDmabufModule(),
            new SinglePixelBufferModule(),
            new RelativePointerModule(),
            new VirtualKeyboardModule(),
            new VirtualPointerModule(),
            new CursorShapeModule(),
            new IdleModule(),
            new KdeServerDecorationModule(),
            new ColorManagementModule(),
            new ColorRepresentationModule(),
            new AlphaModifierModule(),
        ]);
}
