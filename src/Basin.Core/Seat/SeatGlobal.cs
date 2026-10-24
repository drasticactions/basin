using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// A <c>wl_seat</c> global with no capabilities. Each request for a pointer, keyboard or touch device gets the <c>missing_capability</c> error.
/// </summary>
public sealed class SeatGlobal : IDisposable
{
    /// <summary>The <c>wl_seat</c> version that this global advertises.</summary>
    public const int Version = 5;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal _global;
    private readonly string _name;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SeatGlobal"/> class and creates the global.</summary>
    /// <param name="display">The display that gets the global.</param>
    /// <param name="name">The seat name that version 2 and later clients receive.</param>
    public SeatGlobal(WlServerDisplay display, string name = "seat0")
    {
        _display = display;
        _name = name;
        _global = display.CreateGlobal(WlSeat.Interface, Version, OnBind);
    }

    /// <summary>Gets the registry name that a client sees for the global.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The numeric name in that client's registry.</returns>
    public uint NameFor(WlClient client) => _global.NameFor(client);

    /// <summary>Destroys the global at once. A second call does nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _global.Dispose();
    }

    /// <summary>
    /// Removes the global from the registry now and destroys it later through <see cref="GlobalRetirement.Retire"/>.
    /// </summary>
    /// <param name="graceMillis">The maximum delay in milliseconds before the global is destroyed.</param>
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
