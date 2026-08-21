using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

internal sealed class RiverInputDevice
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

    internal void Bind(RiverInputDeviceV1Resource resource)
    {
        _resource = resource;

        resource.AssignToSeat += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Name))
            {
                return;
            }

            AssignTo(_owner.HasSeat(e.Name) ? e.Name : RiverInputManager.DefaultSeatName);
        };

        resource.SetRepeatInfo += (_, e) =>
        {
            if (e.Rate < 0 || e.Delay < 0)
            {
                resource.PostError(
                    (uint)RiverInputDeviceV1.Error.InvalidRepeatInfo,
                    "repeat rate and delay must be non-negative");
                return;
            }

            _owner.RaiseRepeatInfo(this, e.Rate, e.Delay);
        };

        resource.SetScrollFactor += (_, e) =>
        {
            var factor = e.Factor / 256.0;
            if (factor < 0)
            {
                resource.PostError(
                    (uint)RiverInputDeviceV1.Error.InvalidScrollFactor,
                    "the scroll factor must not be negative");
                return;
            }

            _owner.RaiseScrollFactor(this, factor);
        };

        resource.MapToOutput += (_, e) =>
        {
            _owner.RaiseMappedToOutput(this, _owner.Manager.OutputForWlResource(e.Output));
        };

        resource.MapToRectangle += (_, e) =>
        {
            if (e.Width <= 0 || e.Height <= 0)
            {
                resource.PostError(
                    (uint)RiverInputDeviceV1.Error.InvalidMapToRectangle,
                    "the rectangle must have positive width and height");
                return;
            }

            _owner.RaiseMappedToRectangle(this, new Box(e.X, e.Y, e.Width, e.Height));
        };

        resource.DestroyRequest += (_, _) => _resource = null;
    }

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
