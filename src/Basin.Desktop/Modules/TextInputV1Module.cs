using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class TextInputV1Module : DesktopModule<TextInputV1Manager>
{
    public override string WireInterface => "zwp_text_input_manager_v1";

    public override int Version => TextInputV1Manager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ITextInputMethod)];

    protected override TextInputV1Manager Create(BasinServices services) =>
        new(services.Display, services.Find<Seat.Seat>(), services.Find<ITextInputMethod>());
}
