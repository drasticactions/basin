using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>ext_image_copy_capture_manager_v1</c> global through a <see cref="ImageCopyCaptureManager"/>. It copies frames through <see cref="IScreenCapture"/> when one is registered.
/// </summary>
public sealed class ImageCopyCaptureModule : DesktopModule<ImageCopyCaptureManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "ext_image_copy_capture_manager_v1";

    /// <inheritdoc/>
    public override int Version => ImageCopyCaptureManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IScreenCapture)];

    /// <inheritdoc/>
    protected override ImageCopyCaptureManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<ClientBufferRegistry>(),
            services.Find<IScreenCapture>(),
            services.Find<ICaptureDmabufConstraints>());
}
