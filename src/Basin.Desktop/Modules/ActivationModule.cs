using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>xdg_activation_v1</c> global through a <see cref="XdgActivationManager"/>. It requires an <see cref="IActivationTokens"/> service, because the token policy belongs to the compositor.
/// </summary>
public sealed class ActivationModule : DesktopModule<XdgActivationManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "xdg_activation_v1";

    /// <inheritdoc/>
    public override int Version => XdgActivationManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(IActivationTokens)];

    /// <inheritdoc/>
    protected override XdgActivationManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<IActivationTokens>());
}
