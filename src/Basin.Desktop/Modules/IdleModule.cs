using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>ext_idle_notifier_v1</c> global through a <see cref="IdleManager"/>. It reads user activity from <see cref="IIdleSource"/>.
/// </summary>
public sealed class IdleModule : DesktopModule<IdleManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "ext_idle_notifier_v1";

    /// <inheritdoc/>
    public override int Version => 1;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IIdleSource)];

    /// <inheritdoc/>
    protected override IdleManager Create(BasinServices services) =>
        new(services.Display, services.Loop, services.Require<CompositorGlobal>(), services.Find<IIdleSource>());
}
