using Basin.Capabilities;

namespace Basin.Seat;

public static class SeatPack
{
    public static ProtocolPack For(
        string name = "seat0",
        SeatCapability capabilities = SeatCapability.Pointer | SeatCapability.Keyboard) =>
        new([new SeatModule(name, capabilities), new DataDeviceModule()]);
}
