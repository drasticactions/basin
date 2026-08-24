namespace Basin.Plasma;

public static class PlasmaPack
{
    public static ProtocolPack Default => new(
    [
        new PlasmaOutputDeviceModule(),
        new PlasmaOutputManagementModule(),
        new ExternalBrightnessModule(),
        new OutputOrderModule(),
        new DpmsModule(),
        new KdeIdleModule(),
        new KeyStateModule(),
        new FakeInputModule(),
        new TextInputV2Module(),
        new AppMenuModule(),
        new ServerDecorationPaletteModule(),
        new PlasmaShellModule(),
        new ScreenEdgeModule(),
        new LockscreenOverlayModule(),
        new ShadowModule(),
        new SlideModule(),
        new ScreencastModule(),
    ]);
}
