using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_cursor_shape_manager_v1</c> global through a <see cref="CursorShapeManager"/>. It reads <see cref="ICursorTheme"/> when one is registered.
/// </summary>
public sealed class CursorShapeModule : DesktopModule<CursorShapeManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_cursor_shape_manager_v1";

    /// <inheritdoc/>
    public override int Version => CursorShapeManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(ICursorTheme)];

    /// <inheritdoc/>
    protected override CursorShapeManager Create(BasinServices services) =>
        new(services.Display, services.Find<ICursorTheme>(), services.Find<Seat.Seat>(), services.Find<TabletManager>());
}
