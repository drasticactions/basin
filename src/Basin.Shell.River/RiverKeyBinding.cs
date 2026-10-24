using Basin.Shell.River.Protocol;
using Wayland.Server;
using Xkb;

namespace Basin.Shell.River;

internal sealed class RiverKeyBinding : IRiverXkbBindingV1Handler
{
    private readonly RiverWindowManager _manager;
    private readonly RiverBindings _bindings;
    private RiverXkbBindingV1Resource? _resource;

    internal RiverKeyBinding(
        RiverWindowManager manager,
        RiverBindings bindings,
        RiverSeat seat,
        WlClient client,
        uint version,
        uint id,
        uint keysym,
        RiverSeatV1.Modifiers modifiers)
    {
        _manager = manager;
        _bindings = bindings;
        Seat = seat;
        Keysym = keysym;
        Modifiers = modifiers;
        _resource = new RiverXkbBindingV1Resource(client, version, id, this);
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

    void IRiverXkbBindingV1Handler.Enable(RiverXkbBindingV1Resource resource)
    {
        if (_manager.EnsureWindowing())
        {
            IsEnabled = true;
        }
    }

    void IRiverXkbBindingV1Handler.Disable(RiverXkbBindingV1Resource resource)
    {
        if (_manager.EnsureWindowing())
        {
            IsEnabled = false;
        }
    }

    void IRiverXkbBindingV1Handler.SetLayoutOverride(RiverXkbBindingV1Resource resource, in RiverXkbBindingV1Resource.SetLayoutOverrideEventArgs args)
    {
        if (_manager.EnsureWindowing())
        {
            LayoutOverride = args.Layout;
        }
    }

    void IRiverXkbBindingV1Handler.Destroy(RiverXkbBindingV1Resource resource)
    {
        _resource = null;
        _bindings.ForgetBinding(this);
    }
}
