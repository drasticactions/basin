using Basin.Capabilities;

namespace Basin.Seat;

public sealed class SeatModule : IProtocolModule
{
    private readonly string _name;
    private readonly SeatCapability _capabilities;

    public SeatModule(string name = "seat0", SeatCapability capabilities = SeatCapability.Pointer | SeatCapability.Keyboard)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _name = name;
        _capabilities = capabilities;
    }

    public string WireInterface => "wl_seat";

    public int Version => Seat.Version;

    public Seat? Seat { get; private set; }

    public SeatIdleSource? IdleSource { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Seat = new Seat(services.Display, services.Require<CompositorGlobal>(), _name, _capabilities);
        services.Use(Seat);

        if (services.Find<IKeymapSource>() is { } keymaps)
        {
            Seat.Keyboard.KeymapSource = keymaps;
        }
        else
        {
            services.Use(Seat.Keyboard.KeymapSource);
        }

        IdleSource = new SeatIdleSource();
        services.UseDefault<ISelectionStore>(new SeatSelectionStore(Seat));

        if (services.Find<IInputSink>() is SeatInputSink sink)
        {
            sink.Bind(Seat);
        }
        else
        {
            services.UseDefault<IInputSink>(new SeatInputSink(Seat));
        }

        services.UseDefault<IIdleSource>(IdleSource);
        return Seat;
    }
}
