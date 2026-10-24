using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_security_context_manager_v1</c> global through a <see cref="SecurityContextManager"/>.
/// </summary>
public sealed class SecurityContextModule : DesktopModule<SecurityContextManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_security_context_manager_v1";

    /// <inheritdoc/>
    public override int Version => SecurityContextManager.Version;

    /// <inheritdoc/>
    protected override SecurityContextManager Create(BasinServices services) =>
        new(services.Display, services.Loop);
}
