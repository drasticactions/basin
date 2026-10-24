using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_color_representation_manager_v1</c> global through a <see cref="ColorRepresentationManager"/>.
/// </summary>
public sealed class ColorRepresentationModule : DesktopModule<ColorRepresentationManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_color_representation_manager_v1";

    /// <inheritdoc/>
    public override int Version => ColorRepresentationManager.Version;

    /// <inheritdoc/>
    protected override ColorRepresentationManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<ISurfaceHints>() as SurfaceHints);
}
