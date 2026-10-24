using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwlr_gamma_control_manager_v1</c> global through a <see cref="GammaControlManager"/>. It applies gamma ramps through <see cref="IOutputGamma"/> when one is registered.
/// </summary>
public sealed class GammaControlModule : DesktopModule<GammaControlManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwlr_gamma_control_manager_v1";

    /// <inheritdoc/>
    public override int Version => GammaControlManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputGamma)];

    /// <inheritdoc/>
    protected override GammaControlManager Create(BasinServices services) =>
        new(services.Display, services.Find<IOutputGamma>());
}
