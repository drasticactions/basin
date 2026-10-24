using Basin.Diagnostics;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

internal sealed class SeatClient : IWlSeatHandler, IWlPointerHandler, IWlDataDeviceHandler
{
    private readonly Seat _seat;
    private readonly Value120Accumulator[] _value120 = new Value120Accumulator[2];

    internal SeatClient(Seat seat, WlClient client)
    {
        _seat = seat;
        Client = client;
    }

    public WlClient Client { get; }

    public List<WlPointerResource> Pointers { get; } = [];

    public List<WlKeyboardResource> Keyboards { get; } = [];

    public List<WlTouchResource> Touches { get; } = [];

    public List<WlDataDeviceResource> DataDevices { get; } = [];

    public bool IsEmpty =>
        Pointers.Count == 0 && Keyboards.Count == 0 && Touches.Count == 0 && DataDevices.Count == 0;

    public void AccumulateValue120(in PointerAxis axis, out double lowResValue, out int lowResSteps)
    {
        lowResValue = 0;
        lowResSteps = 0;
        if (axis.Value120 == 0)
        {
            return;
        }

        ref var acc = ref _value120[(int)axis.Axis];
        if (acc.LastSteps == 0 || (axis.Value120 < 0) != (acc.LastSteps < 0))
        {
            acc.Steps = 0;
            acc.Value = 0;
        }

        acc.Steps += axis.Value120;
        acc.LastSteps = axis.Value120;
        acc.Value += axis.Value;

        lowResSteps = acc.Steps / 120;
        if (lowResSteps == 0)
        {
            return;
        }

        acc.Steps -= lowResSteps * 120;
        lowResValue = acc.Value;
        acc.Value = 0;
    }

    private struct Value120Accumulator
    {
        public int Steps;
        public int LastSteps;
        public double Value;
    }

    void IWlSeatHandler.GetPointer(WlSeatResource resource, in WlSeatResource.GetPointerEventArgs args) =>
        AddPointer(new WlPointerResource(Client, resource.Version, args.Id, this));

    void IWlSeatHandler.GetKeyboard(WlSeatResource resource, in WlSeatResource.GetKeyboardEventArgs args) =>
        AddKeyboard(new WlKeyboardResource(Client, resource.Version, args.Id));

    void IWlSeatHandler.GetTouch(WlSeatResource resource, in WlSeatResource.GetTouchEventArgs args) =>
        AddTouch(new WlTouchResource(Client, resource.Version, args.Id));

    void IWlSeatHandler.Release(WlSeatResource resource)
    {
    }

    void IWlPointerHandler.SetCursor(WlPointerResource resource, in WlPointerResource.SetCursorEventArgs args) =>
        _seat.Pointer.HandleSetCursor(this, args);

    void IWlPointerHandler.Release(WlPointerResource resource)
    {
    }

    void IWlDataDeviceHandler.SetSelection(WlDataDeviceResource resource, in WlDataDeviceResource.SetSelectionEventArgs args) =>
        _seat.DataDevice.OnSetSelection(in args);

    void IWlDataDeviceHandler.StartDrag(WlDataDeviceResource resource, in WlDataDeviceResource.StartDragEventArgs args) =>
        _seat.DataDevice.HandleStartDrag(this, in args);

    void IWlDataDeviceHandler.Release(WlDataDeviceResource resource)
    {
    }

    public void AddPointer(WlPointerResource pointer)
    {
        Pointers.Add(pointer);
        pointer.Destroyed += (_, _) =>
        {
            Pointers.Remove(pointer);
            _seat.PruneClient(this);
        };
        _seat.Pointer.InitializeResource(this, pointer);
    }

    public void AddKeyboard(WlKeyboardResource keyboard)
    {
        Keyboards.Add(keyboard);
        keyboard.Destroyed += (_, _) =>
        {
            Keyboards.Remove(keyboard);
            _seat.PruneClient(this);
        };
        _seat.Keyboard.InitializeResource(keyboard);
    }

    public void AddTouch(WlTouchResource touch)
    {
        Touches.Add(touch);
        touch.Destroyed += (_, _) =>
        {
            Touches.Remove(touch);
            _seat.PruneClient(this);
        };
    }

    public void AddDataDevice(WlDataDeviceResource dataDevice)
    {
        DataDevices.Add(dataDevice);
        dataDevice.Destroyed += (_, _) =>
        {
            DataDevices.Remove(dataDevice);
            _seat.PruneClient(this);
        };
    }
}
