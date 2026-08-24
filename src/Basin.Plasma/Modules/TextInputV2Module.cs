using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class TextInputV2Module : PlasmaModule<TextInputV2Manager>
{
    public override string WireInterface => "zwp_text_input_manager_v2";

    public override int Version => TextInputV2Manager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(ITextInputMethod)];

    protected override TextInputV2Manager Create(BasinServices services) =>
        new(services.Display, services.Find<Basin.Seat.Seat>(), services.Find<ITextInputMethod>());
}
