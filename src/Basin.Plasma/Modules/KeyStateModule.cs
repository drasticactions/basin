namespace Basin.Plasma;

public sealed class KeyStateModule : PlasmaModule<KeyStateManager>
{
    public override string WireInterface => "org_kde_kwin_keystate";

    public override int Version => KeyStateManager.Version;

    protected override KeyStateManager Create(BasinServices services) =>
        new(services.Display, services.Find<Seat.Seat>());
}
