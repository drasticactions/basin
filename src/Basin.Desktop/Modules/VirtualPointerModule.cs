using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class VirtualPointerModule : DesktopModule<VirtualPointerManager>
{
    public override string WireInterface => "zwlr_virtual_pointer_manager_v1";

    public override int Version => VirtualPointerManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IInputSink)];

    protected override VirtualPointerManager Create(BasinServices services) =>
        new(services.Display, services.Find<IInputSink>());
}
