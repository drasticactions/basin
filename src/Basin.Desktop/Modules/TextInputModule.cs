using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class TextInputModule : DesktopModule<TextInputManager>
{
    public override string WireInterface => "zwp_text_input_manager_v3";

    public override int Version => TextInputManager.TextInputVersion;

    public override IReadOnlyList<Type> Capabilities => [typeof(ITextInputMethod)];

    protected override TextInputManager Create(BasinServices services) =>
        new(services.Display, services.Find<Seat.Seat>(), services.Find<ITextInputMethod>());
}
