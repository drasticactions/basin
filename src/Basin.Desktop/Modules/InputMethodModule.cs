using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_input_method_manager_v2</c> global through an <see cref="InputMethodRelay"/>. The relay becomes the default <see cref="ITextInputMethod"/> for the text-input protocols.
/// </summary>
public sealed class InputMethodModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "zwp_input_method_manager_v2";

    /// <inheritdoc/>
    public int Version => InputMethodRelay.Version;

    /// <summary>
    /// Gets the relay that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public InputMethodRelay? Relay { get; private set; }

    /// <summary>
    /// Gets or sets the input method that receives text input while no client input method is bound. Set it before the install, because the relay reads it only once.
    /// </summary>
    public ITextInputMethod? Fallback { get; set; }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Relay = new InputMethodRelay(services.Display, services.Find<Seat.Seat>(), Fallback);
        services.Use(Relay);
        services.UseDefault<ITextInputMethod>(Relay);
        return Relay;
    }
}
