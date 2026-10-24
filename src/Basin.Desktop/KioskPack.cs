using Basin.Shell.Xdg;

namespace Basin.Desktop;

/// <summary>
/// Builds the smallest protocol set for a single-application kiosk compositor.
/// </summary>
public static class KioskPack
{
    /// <summary>
    /// Gets the core pack without <c>wl_fixes</c>, the seat pack, xdg-shell and a small set of output, capture and input modules.
    /// </summary>
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
