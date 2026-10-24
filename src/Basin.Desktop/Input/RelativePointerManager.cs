using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwp_relative_pointer_manager_v1</c> global and sends relative motion to the client with pointer focus. Until the consumer calls <see cref="NotifyMotion"/>, it follows pointer motion and reports it as both accelerated and unaccelerated.
/// </summary>
public sealed class RelativePointerManager : IDisposable
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly Seat.Seat _seat;
    private readonly List<ZwpRelativePointerV1Resource> _pointers = [];
    private bool _consumerNotifies;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelativePointerManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="seat">The seat whose pointer focus selects the client.</param>
    public RelativePointerManager(WlServerDisplay display, Seat.Seat seat)
    {
        _seat = seat;
        _global = display.CreateGlobal(ZwpRelativePointerManagerV1.Interface, Version, OnBind);
        _seat.Pointer.Moved += FollowPointer;
    }

    /// <summary>
    /// Stops following the pointer and removes the global.
    /// </summary>
    public void Dispose()
    {
        _seat.Pointer.Moved -= FollowPointer;
        _global.Dispose();
    }

    /// <summary>
    /// Sends relative motion to the focused client and then a pointer frame. After the first call, the manager stops following pointer motion.
    /// </summary>
    /// <param name="timeMicroseconds">The event time in microseconds.</param>
    /// <param name="dx">The accelerated X delta in logical pixels.</param>
    /// <param name="dy">The accelerated Y delta in logical pixels.</param>
    /// <param name="dxUnaccelerated">The X delta before acceleration.</param>
    /// <param name="dyUnaccelerated">The Y delta before acceleration.</param>
    public void NotifyMotion(ulong timeMicroseconds, double dx, double dy, double dxUnaccelerated, double dyUnaccelerated)
    {
        _consumerNotifies = true;
        Send(timeMicroseconds, dx, dy, dxUnaccelerated, dyUnaccelerated);
    }

    private void FollowPointer(uint timeMilliseconds, double dx, double dy)
    {
        if (_consumerNotifies || _pointers.Count == 0 || (dx == 0 && dy == 0))
        {
            return;
        }

        Send((ulong)timeMilliseconds * 1000, dx, dy, dx, dy);
    }

    private void Send(ulong timeMicroseconds, double dx, double dy, double dxUnaccelerated, double dyUnaccelerated)
    {
        var focus = _seat.Pointer.Focus;
        if (focus is null)
        {
            return;
        }

        var client = focus.Resource.Client;
        var scale = focus.ServerScale;
        dx *= scale;
        dy *= scale;
        dxUnaccelerated *= scale;
        dyUnaccelerated *= scale;
        foreach (var pointer in _pointers)
        {
            if (!pointer.IsDestroyed && pointer.Client == client)
            {
                pointer.SendRelativeMotion(
                    (uint)(timeMicroseconds >> 32),
                    (uint)timeMicroseconds,
                    WlFixed.FromDouble(dx),
                    WlFixed.FromDouble(dy),
                    WlFixed.FromDouble(dxUnaccelerated),
                    WlFixed.FromDouble(dyUnaccelerated));
            }
        }

        _seat.Pointer.NotifyFrame();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpRelativePointerManagerV1Resource(client, version, id);
        manager.GetRelativePointer += (_, e) =>
        {
            var pointer = new ZwpRelativePointerV1Resource(client, manager.Version, e.Id);
            _pointers.Add(pointer);
            pointer.Destroyed += (_, _) => _pointers.Remove(pointer);
        };
    }
}
