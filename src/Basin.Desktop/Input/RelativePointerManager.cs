using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class RelativePointerManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly Seat.Seat _seat;
    private readonly List<ZwpRelativePointerV1Resource> _pointers = [];
    private bool _consumerNotifies;

    public RelativePointerManager(WlServerDisplay display, Seat.Seat seat)
    {
        _seat = seat;
        _global = display.CreateGlobal(ZwpRelativePointerManagerV1.Interface, Version, OnBind);
        _seat.Pointer.Moved += FollowPointer;
    }

    public void Dispose()
    {
        _seat.Pointer.Moved -= FollowPointer;
        _global.Dispose();
    }

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
