using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class LockscreenOverlayModule : PlasmaModule<LockscreenOverlayManager>
{
    private readonly LockOverlaySurfaces _allowed = new();

    public override string WireInterface => "kde_lockscreen_overlay_v1";

    public override int Version => LockscreenOverlayManager.Version;

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ILockOverlaySurfaces>(_allowed);
    }

    protected override LockscreenOverlayManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), _allowed);
}
