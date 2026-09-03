using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;

namespace Basin.Hypr;

public sealed class HyprlandLockNotifyModule : DesktopModule<HyprlandLockNotifier>
{
    public override string WireInterface => "hyprland_lock_notifier_v1";

    public override int Version => HyprlandLockNotifier.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ILockState)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ILockState>(NeverLocked.Instance);
    }

    protected override HyprlandLockNotifier Create(BasinServices services) =>
        new(services.Display, services.Find<ILockState>());
}
