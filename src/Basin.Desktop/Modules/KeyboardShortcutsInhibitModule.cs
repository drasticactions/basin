using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_keyboard_shortcuts_inhibit_manager_v1</c> global through a <see cref="KeyboardShortcutsInhibitManager"/>.
/// </summary>
public sealed class KeyboardShortcutsInhibitModule : DesktopModule<KeyboardShortcutsInhibitManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_keyboard_shortcuts_inhibit_manager_v1";

    /// <inheritdoc/>
    public override int Version => KeyboardShortcutsInhibitManager.Version;

    /// <inheritdoc/>
    protected override KeyboardShortcutsInhibitManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<Seat.Seat>());
}
