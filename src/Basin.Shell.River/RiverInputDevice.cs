using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

internal sealed class RiverInputDevice : IRiverInputDeviceV1Handler
{
    private readonly RiverInputManager _owner;
    private RiverInputDeviceV1Resource? _resource;

    internal RiverInputDevice(RiverInputManager owner, object handle, string name, InputDeviceType type)
    {
        _owner = owner;
        Handle = handle;
        Name = name;
        Type = type;
    }

    internal object Handle { get; }

    internal string Name { get; }

    internal InputDeviceType Type { get; }

    internal string SeatName { get; private set; } = RiverInputManager.DefaultSeatName;

    internal RiverInputDeviceV1Resource? Resource => _resource;

    internal RiverInputDeviceV1Resource Bind(WlClient client, uint version, uint id)
    {
        var resource = new RiverInputDeviceV1Resource(client, version, id, this);
        _resource = resource;
        return resource;
    }

    void IRiverInputDeviceV1Handler.AssignToSeat(RiverInputDeviceV1Resource resource, in RiverInputDeviceV1Resource.AssignToSeatEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Name))
        {
            return;
        }

        AssignTo(_owner.HasSeat(args.Name) ? args.Name : RiverInputManager.DefaultSeatName);
    }

    void IRiverInputDeviceV1Handler.SetRepeatInfo(RiverInputDeviceV1Resource resource, in RiverInputDeviceV1Resource.SetRepeatInfoEventArgs args)
    {
        if (args.Rate < 0 || args.Delay < 0)
        {
            resource.PostError(
                (uint)RiverInputDeviceV1.Error.InvalidRepeatInfo,
                "The repeat rate and delay must not be negative.");
            return;
        }

        _owner.RaiseRepeatInfo(this, args.Rate, args.Delay);
    }

    void IRiverInputDeviceV1Handler.SetScrollFactor(RiverInputDeviceV1Resource resource, in RiverInputDeviceV1Resource.SetScrollFactorEventArgs args)
    {
        var factor = args.Factor / 256.0;
        if (factor < 0)
        {
            resource.PostError(
                (uint)RiverInputDeviceV1.Error.InvalidScrollFactor,
                "The scroll factor must not be negative.");
            return;
        }

        _owner.RaiseScrollFactor(this, factor);
    }

    void IRiverInputDeviceV1Handler.MapToOutput(RiverInputDeviceV1Resource resource, in RiverInputDeviceV1Resource.MapToOutputEventArgs args)
    {
        _owner.RaiseMappedToOutput(this, _owner.Manager.OutputForWlResource(args.Output));
    }

    void IRiverInputDeviceV1Handler.MapToRectangle(RiverInputDeviceV1Resource resource, in RiverInputDeviceV1Resource.MapToRectangleEventArgs args)
    {
        if (args.Width <= 0 || args.Height <= 0)
        {
            resource.PostError(
                (uint)RiverInputDeviceV1.Error.InvalidMapToRectangle,
                "The rectangle width and height must be more than zero.");
            return;
        }

        _owner.RaiseMappedToRectangle(this, new Box(args.X, args.Y, args.Width, args.Height));
    }

    void IRiverInputDeviceV1Handler.Destroy(RiverInputDeviceV1Resource resource) => _resource = null;

    internal void AssignTo(string seatName)
    {
        if (SeatName == seatName)
        {
            return;
        }

        SeatName = seatName;
        _owner.RaiseAssigned(this);
    }

    internal void SendProperties(uint version)
    {
        if (_resource is not { IsDestroyed: false } resource)
        {
            return;
        }

        resource.SendType((RiverInputDeviceV1.Type)Type);
        resource.SendName(Name);
        if (version >= 2)
        {
            resource.SendDone();
        }
    }

    internal void SendRemoved()
    {
        if (_resource is { IsDestroyed: false } resource)
        {
            resource.SendRemoved();
        }

        _resource = null;
    }

    internal void ResetForNewManager()
    {
        _resource = null;
        SeatName = RiverInputManager.DefaultSeatName;
    }
}
