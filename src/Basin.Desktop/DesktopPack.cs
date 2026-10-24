using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Builds the protocol packs for a full desktop compositor. The packs list modules only. A consumer must register the capabilities that the modules call.
/// </summary>
public static class DesktopPack
{
    /// <summary>
    /// Gets the core, seat, xdg-shell and desktop modules, with no session store for session management.
    /// </summary>
    public static ProtocolPack Default => For(null);

    /// <summary>
    /// Builds the core, seat, xdg-shell and desktop modules. A non-null <paramref name="appName"/> seeds a <see cref="FileSessionStore"/> for session management.
    /// </summary>
    /// <param name="appName">The application name that names the session store directory, or <see langword="null"/> for no default store.</param>
    /// <returns>A new pack that holds every desktop module.</returns>
    public static ProtocolPack For(string? appName) =>
        CorePack.Default + Seat.SeatPack.For() + Shell.Xdg.XdgPack.Default + DesktopFor(appName);

    /// <summary>
    /// Gets the desktop modules only, without the core, seat and xdg-shell modules. The syncobj module is in the pack only when the platform supports DRM sync objects.
    /// </summary>
    public static ProtocolPack Desktop => DesktopFor(null);

    private static ProtocolPack DesktopFor(string? appName)
    {
        List<IProtocolModule> modules =
        [
            new XdgOutputModule(),
            new FullscreenShellModule(),
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
            new GamescopeLimiterModule(),
            new PointerWarpModule(),
            new SessionManagementModule(appName),
            new ContentTypeModule(),
            new FractionalScaleModule(),
            new SinglePixelBufferModule(),
            new SecurityContextModule(),
        ];

        if (PlatformFacts.HasSyncobj)
        {
            modules.Add(new LinuxDrmSyncobjModule());
        }

        return new ProtocolPack(modules);
    }
}
