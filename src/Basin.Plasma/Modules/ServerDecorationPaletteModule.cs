namespace Basin.Plasma;

public sealed class ServerDecorationPaletteModule : PlasmaModule<ServerDecorationPaletteManager>
{
    public override string WireInterface => "org_kde_kwin_server_decoration_palette_manager";

    public override int Version => ServerDecorationPaletteManager.Version;

    protected override ServerDecorationPaletteManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>());
}
