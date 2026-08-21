using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class CursorShapeModule : DesktopModule<CursorShapeManager>
{
    public override string WireInterface => "wp_cursor_shape_manager_v1";

    public override int Version => CursorShapeManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ICursorTheme)];

    protected override CursorShapeManager Create(BasinServices services) =>
        new(services.Display, services.Find<ICursorTheme>(), services.Find<Seat.Seat>());
}
