using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class SinglePixelBufferModule : DesktopModule<SinglePixelBufferManager>
{
    public override string WireInterface => "wp_single_pixel_buffer_manager_v1";

    public override int Version => SinglePixelBufferManager.Version;

    protected override SinglePixelBufferManager Create(BasinServices services) =>
        new(services.Display, services.Require<ClientBufferRegistry>());
}
