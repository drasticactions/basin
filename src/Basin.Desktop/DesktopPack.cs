using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public static class DesktopPack
{
    public static ProtocolPack Default => For(null);

    public static ProtocolPack For(string? appName) =>
        CorePack.Default + Seat.SeatPack.For() + Shell.Xdg.XdgPack.Default + DesktopFor(appName);

    public static ProtocolPack Desktop => DesktopFor(null);

    private static ProtocolPack DesktopFor(string? appName)
    {
        List<IProtocolModule> modules =
        [
            new XdgOutputModule(),
            new SessionLockModule(),
            new ScreencopyModule(),
            new ImageCaptureSourceModule(),
            new ImageCopyCaptureModule(),
            new ExportDmabufModule(),
            new DataControlModule(),
            new ExtDataControlModule(),
            new PrimarySelectionModule(),
            new OutputManagementModule(),
            new OutputPowerModule(),
            new GammaControlModule(),
            new DrmLeaseModule(),
            new ForeignToplevelModule(),
            new ForeignToplevelListModule(),
            new WorkspaceModule(),
            new PlasmaVirtualDesktopModule(),
            new PlasmaWindowManagementModule(),
            new RelativePointerModule(),
            new PointerConstraintsModule(),
            new PointerGesturesModule(),
            new KeyboardShortcutsInhibitModule(),
            new VirtualKeyboardModule(),
            new VirtualPointerModule(),
            new TransientSeatModule(),
            new TabletModule(),
            new CursorShapeModule(),
            new InputMethodModule(),
            new TextInputModule(),
            new TextInputV1Module(),
            new IdleModule(),
            new ActivationModule(),
            new SystemBellModule(),
            new AlphaModifierModule(),
            new BackgroundEffectModule(),
            new XdgForeignModule(),
            new KdeServerDecorationModule(),
            new ColorManagementModule(),
            new ColorRepresentationModule(),
            new TearingControlModule(),
            new FifoModule(),
            new CommitTimingModule(),
            new PointerWarpModule(),
            new SessionManagementModule(appName),
            new ContentTypeModule(),
            new FractionalScaleModule(),
            new SinglePixelBufferModule(),
            new SecurityContextModule(),
        ];

        if (OperatingSystem.IsLinux())
        {
            modules.Add(new LinuxDrmSyncobjModule());
        }

        return new ProtocolPack(modules);
    }
}
