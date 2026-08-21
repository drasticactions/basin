using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class InputMethodModule : IProtocolModule
{
    public string WireInterface => "zwp_input_method_manager_v2";

    public int Version => InputMethodRelay.Version;

    public InputMethodRelay? Relay { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Relay = new InputMethodRelay(services.Display, services.Find<Seat.Seat>());
        services.Use(Relay);
        services.UseDefault<ITextInputMethod>(Relay);
        return Relay;
    }
}
