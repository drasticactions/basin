using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_text_input_manager_v3</c> global through a <see cref="TextInputManager"/>. It forwards text input to <see cref="ITextInputMethod"/>.
/// </summary>
public sealed class TextInputModule : DesktopModule<TextInputManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_text_input_manager_v3";

    /// <inheritdoc/>
    public override int Version => TextInputManager.TextInputVersion;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(ITextInputMethod)];

    /// <inheritdoc/>
    protected override TextInputManager Create(BasinServices services)
    {
        var manager = new TextInputManager(services.Display, services.Find<Seat.Seat>(), services.Find<ITextInputMethod>());
        services.UseDefault<ITextInputCommit>(manager);
        return manager;
    }
}
