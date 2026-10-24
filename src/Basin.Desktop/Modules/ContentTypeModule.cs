using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_content_type_manager_v1</c> global through a <see cref="ContentTypeManager"/>.
/// </summary>
public sealed class ContentTypeModule : DesktopModule<ContentTypeManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_content_type_manager_v1";

    /// <inheritdoc/>
    public override int Version => ContentTypeManager.Version;

    /// <inheritdoc/>
    protected override ContentTypeManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Find<ISurfaceHints>() as SurfaceHints);
}
