using Wayland;
using Wayland.Server;

namespace Basin;

public sealed class SeatGlobal : IDisposable
{
    public const int Version = 5;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal _global;
    private readonly string _name;
    private bool _disposed;

    public SeatGlobal(WlServerDisplay display, string name = "seat0")
    {
        _display = display;
        _name = name;
        _global = display.CreateGlobal(WlSeat.Interface, Version, OnBind);
    }

    public uint NameFor(WlClient client) => _global.NameFor(client);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _global.Dispose();
    }

    public void Retire(int graceMillis = GlobalRetirement.DefaultGraceMillis) =>
        GlobalRetirement.Retire(_display, _global, Dispose, graceMillis);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var seat = new WlSeatResource(client, version, id);
        seat.SendCapabilities(default);
        if (version >= 2)
        {
            seat.SendName(_name);
        }

        seat.GetPointer += (_, _) => PostMissingCapability(seat, "pointer");
        seat.GetKeyboard += (_, _) => PostMissingCapability(seat, "keyboard");
        seat.GetTouch += (_, _) => PostMissingCapability(seat, "touch");
    }

    private static void PostMissingCapability(WlSeatResource seat, string what)
    {
        seat.PostError(0, $"seat has no {what} capability");
    }
}
