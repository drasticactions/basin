using Basin.Shell.River.Protocol;
using Wayland.Server;
using Xkb;

namespace Basin.Shell.River;

internal sealed class RiverKeyBinding
{
    private readonly RiverWindowManager _manager;
    private RiverXkbBindingV1Resource? _resource;

    internal RiverKeyBinding(
        RiverWindowManager manager,
        RiverSeat seat,
        RiverXkbBindingV1Resource resource,
        uint keysym,
        RiverSeatV1.Modifiers modifiers)
    {
        _manager = manager;
        _resource = resource;
        Seat = seat;
        Keysym = keysym;
        Modifiers = modifiers;

        resource.Enable += (_, _) =>
        {
            if (_manager.EnsureWindowing())
            {
                IsEnabled = true;
            }
        };
        resource.Disable += (_, _) =>
        {
            if (_manager.EnsureWindowing())
            {
                IsEnabled = false;
            }
        };
        resource.SetLayoutOverride += (_, e) =>
        {
            if (_manager.EnsureWindowing())
            {
                LayoutOverride = e.Layout;
            }
        };
        resource.DestroyRequest += (_, _) => _resource = null;
    }

    internal RiverSeat Seat { get; }

    internal uint Keysym { get; }

    internal RiverSeatV1.Modifiers Modifiers { get; }

    internal uint? LayoutOverride { get; private set; }

    internal bool IsEnabled { get; private set; }

    internal void SendPressed() => Alive()?.SendPressed();

    internal void SendReleased() => Alive()?.SendReleased();

    internal bool SendStopRepeat()
    {
        if (Alive() is not { Version: >= 2 } resource)
        {
            return false;
        }

        resource.SendStopRepeat();
        return true;
    }

    internal void MakeInert()
    {
        _resource = null;
        IsEnabled = false;
    }

    private RiverXkbBindingV1Resource? Alive() =>
        _resource is { IsDestroyed: false } resource ? resource : null;
}
