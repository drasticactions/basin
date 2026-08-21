using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ImageCopyCaptureModule : DesktopModule<ImageCopyCaptureManager>
{
    public override string WireInterface => "ext_image_copy_capture_manager_v1";

    public override int Version => ImageCopyCaptureManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IScreenCapture)];

    protected override ImageCopyCaptureManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<ClientBufferRegistry>(),
            services.Find<IScreenCapture>(),
            services.Find<ICaptureDmabufConstraints>());
}
