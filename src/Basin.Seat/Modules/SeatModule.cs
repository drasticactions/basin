using Basin.Capabilities;

namespace Basin.Seat;

/// <summary>
/// Installs the <c>wl_seat</c> global and registers the <see cref="Seat"/> with its default capabilities.
/// A capability that the consumer registers wins over the default that this module seeds.
/// </summary>
public sealed class SeatModule : IProtocolModule
{
    private readonly string _name;
    private readonly SeatCapability _capabilities;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeatModule"/> class.
    /// </summary>
    /// <param name="name">The seat name that clients see. It must not be null or empty.</param>
    /// <param name="capabilities">The input device kinds that the seat advertises.</param>
    public SeatModule(string name = "seat0", SeatCapability capabilities = SeatCapability.Pointer | SeatCapability.Keyboard)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _name = name;
        _capabilities = capabilities;
    }

    /// <inheritdoc/>
    public string WireInterface => "wl_seat";

    /// <inheritdoc/>
    public int Version => Seat.Version;

    /// <summary>
    /// Gets the seat that <see cref="Install"/> created, or <see langword="null"/> before the install.
    /// </summary>
    public Seat? Seat { get; private set; }

    /// <summary>
    /// Gets the idle source that <see cref="Install"/> created, or <see langword="null"/> before the install.
    /// It is the default <see cref="IIdleSource"/>.
    /// </summary>
    public SeatIdleSource? IdleSource { get; private set; }

    /// <summary>
    /// Creates the seat and registers it, its keymap source and its default capabilities.
    /// A registered <see cref="IKeymapSource"/> replaces the keymap source of the seat. A registered <see cref="SeatInputSink"/> is bound to the seat.
    /// </summary>
    /// <param name="services">The registry to read from and to add the seat to.</param>
    /// <returns>The seat. Disposing it removes the global.</returns>
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
        services.UseDefault<IActiveKeymap>(Seat.Keyboard);
        services.UseDefault<IKeymapLookup>(Seat.Keyboard);
        services.UseDefault<IKeymapOverride>(Seat.Keyboard);

        if (services.Find<IInputSink>() is SeatInputSink sink)
        {
            sink.Bind(Seat, services.Find<OutputLayout>());
        }
        else
        {
            services.UseDefault<IInputSink>(new SeatInputSink(Seat, services.Find<OutputLayout>()));
        }

        services.UseDefault<IIdleSource>(IdleSource);
        services.UseDefault<ISyntheticInput>(new SeatSyntheticInput(Seat, services));
        services.UseDefault<IInputDiagnostics>(new SeatDiagnostics(Seat, services));
        return Seat;
    }
}
