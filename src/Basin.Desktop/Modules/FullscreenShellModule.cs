using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_fullscreen_shell_v1</c> global through a <see cref="FullscreenShellGlobal"/>.
/// </summary>
public sealed class FullscreenShellModule : DesktopModule<FullscreenShellGlobal>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_fullscreen_shell_v1";

    /// <inheritdoc/>
    public override int Version => FullscreenShellGlobal.Version;

    /// <inheritdoc/>
    protected override FullscreenShellGlobal Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Require<OutputLayout>());
}
