using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

internal sealed class RiverPointerBinding : IRiverPointerBindingV1Handler
{
    private readonly RiverWindowManager _manager;
    private readonly RiverSeat _seat;
    private RiverPointerBindingV1Resource? _resource;

    internal RiverPointerBinding(
        RiverWindowManager manager,
        RiverSeat seat,
        WlClient client,
        uint version,
        uint id,
        uint button,
        RiverSeatV1.Modifiers modifiers)
    {
        _manager = manager;
        _seat = seat;
        Button = button;
        Modifiers = modifiers;
        _resource = new RiverPointerBindingV1Resource(client, version, id, this);
    }

    internal uint Button { get; }

    internal RiverSeatV1.Modifiers Modifiers { get; }

    internal bool IsEnabled { get; private set; }

    internal void SendPressed()
    {
        if (_resource is { IsDestroyed: false } resource)
        {
            resource.SendPressed();
        }
    }

    internal void SendReleased()
    {
        if (_resource is { IsDestroyed: false } resource)
        {
            resource.SendReleased();
        }
    }

    void IRiverPointerBindingV1Handler.Enable(RiverPointerBindingV1Resource resource)
    {
        if (_manager.EnsureWindowing())
        {
            IsEnabled = true;
        }
    }

    void IRiverPointerBindingV1Handler.Disable(RiverPointerBindingV1Resource resource)
    {
        if (_manager.EnsureWindowing())
        {
            IsEnabled = false;
        }
    }

    void IRiverPointerBindingV1Handler.Destroy(RiverPointerBindingV1Resource resource)
    {
        _resource = null;
        _seat.ForgetPointerBinding(this);
    }
}
