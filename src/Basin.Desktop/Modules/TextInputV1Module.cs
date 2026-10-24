using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_text_input_manager_v1</c> global through a <see cref="TextInputV1Manager"/>. It forwards text input to <see cref="ITextInputMethod"/>.
/// </summary>
public sealed class TextInputV1Module : DesktopModule<TextInputV1Manager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_text_input_manager_v1";

    /// <inheritdoc/>
    public override int Version => TextInputV1Manager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(ITextInputMethod)];

    /// <inheritdoc/>
    protected override TextInputV1Manager Create(BasinServices services) =>
        new(services.Display, services.Find<Seat.Seat>(), services.Find<ITextInputMethod>());
}
