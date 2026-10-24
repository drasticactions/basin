using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>ext_output_image_capture_source_manager_v1</c> global through a <see cref="ImageCaptureSourceManager"/>. It creates the capture sources that <see cref="ImageCopyCaptureModule"/> consumes.
/// </summary>
public sealed class ImageCaptureSourceModule : DesktopModule<ImageCaptureSourceManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "ext_output_image_capture_source_manager_v1";

    /// <inheritdoc/>
    public override int Version => ImageCaptureSourceManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IScreenCapture), typeof(IToplevelModel)];

    /// <inheritdoc/>
    protected override ImageCaptureSourceManager Create(BasinServices services) => new(services.Display);
}
