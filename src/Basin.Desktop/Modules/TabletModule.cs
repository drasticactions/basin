using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_tablet_manager_v2</c> global through a <see cref="TabletManager"/>. It reads tablet devices from <see cref="ITabletSource"/>.
/// </summary>
public sealed class TabletModule : DesktopModule<TabletManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_tablet_manager_v2";

    /// <inheritdoc/>
    public override int Version => TabletManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(ITabletSource)];

    /// <inheritdoc/>
    protected override TabletManager Create(BasinServices services) =>
        new(services.Display, services.Find<ITabletSource>());
}
