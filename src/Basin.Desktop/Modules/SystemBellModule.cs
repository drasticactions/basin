using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>xdg_system_bell_v1</c> global through a <see cref="SystemBellManager"/>. It requires an <see cref="IBell"/> that rings the bell.
/// </summary>
public sealed class SystemBellModule : DesktopModule<SystemBellManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "xdg_system_bell_v1";

    /// <inheritdoc/>
    public override int Version => SystemBellManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(IBell)];

    /// <inheritdoc/>
    protected override SystemBellManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<IBell>());
}
