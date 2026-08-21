using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class KeyboardShortcutsInhibitModule : DesktopModule<KeyboardShortcutsInhibitManager>
{
    public override string WireInterface => "zwp_keyboard_shortcuts_inhibit_manager_v1";

    public override int Version => KeyboardShortcutsInhibitManager.Version;

    protected override KeyboardShortcutsInhibitManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<Seat.Seat>());
}
