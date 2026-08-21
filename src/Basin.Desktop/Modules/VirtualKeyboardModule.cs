using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class VirtualKeyboardModule : DesktopModule<VirtualKeyboardManager>
{
    public override string WireInterface => "zwp_virtual_keyboard_manager_v1";

    public override int Version => VirtualKeyboardManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IInputSink)];

    protected override VirtualKeyboardManager Create(BasinServices services) =>
        new(services.Display, services.Find<IInputSink>());
}
