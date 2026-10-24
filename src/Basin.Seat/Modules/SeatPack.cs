using Basin.Capabilities;

namespace Basin.Seat;

/// <summary>
/// Builds the protocol pack for one seat.
/// </summary>
public static class SeatPack
{
    /// <summary>
    /// Creates a pack with a <see cref="SeatModule"/> and a <see cref="DataDeviceModule"/>.
    /// </summary>
    /// <param name="name">The seat name that clients see.</param>
    /// <param name="capabilities">The input device kinds that the seat advertises.</param>
    /// <returns>A new pack with the two modules.</returns>
    public static ProtocolPack For(
        string name = "seat0",
        SeatCapability capabilities = SeatCapability.Pointer | SeatCapability.Keyboard) =>
        new([new SeatModule(name, capabilities), new DataDeviceModule()]);
}
