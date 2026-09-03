using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;

namespace Basin.Hypr;

public sealed class HyprlandGlobalShortcutsModule : DesktopModule<HyprlandGlobalShortcutsManager>
{
    public override string WireInterface => "hyprland_global_shortcuts_manager_v1";

    public override int Version => HyprlandGlobalShortcutsManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IGlobalShortcuts)];

    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<IGlobalShortcuts>(new DefaultGlobalShortcuts());
    }

    protected override HyprlandGlobalShortcutsManager Create(BasinServices services) =>
        new(services.Display, services.Require<IGlobalShortcuts>());
}
