using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ImageCaptureSourceModule : DesktopModule<ImageCaptureSourceManager>
{
    public override string WireInterface => "ext_output_image_capture_source_manager_v1";

    public override int Version => ImageCaptureSourceManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IScreenCapture), typeof(IToplevelModel)];

    protected override ImageCaptureSourceManager Create(BasinServices services) => new(services.Display);
}
