using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class ScreencopyModule : DesktopModule<ScreencopyManager>
{
    public override string WireInterface => "zwlr_screencopy_manager_v1";

    public override int Version => ScreencopyManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IScreenCapture)];

    protected override ScreencopyManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<OutputLayout>(),
            services.Require<ClientBufferRegistry>(),
            services.Find<IScreenCapture>(),
            services.Find<ICaptureDmabufConstraints>());
}
