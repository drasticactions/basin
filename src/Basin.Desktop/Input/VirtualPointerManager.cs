using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;
using Xkb;

namespace Basin.Desktop;

public sealed class VirtualPointerManager : IDisposable
{
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly IInputSink? _sink;

    public VirtualPointerManager(WlServerDisplay display, IInputSink? sink)
    {
        ArgumentNullException.ThrowIfNull(display);
        _sink = sink;
        _global = display.CreateGlobal(ZwlrVirtualPointerManagerV1.Interface, Version, OnBind);
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwlrVirtualPointerManagerV1Resource(client, version, id);
        manager.CreateVirtualPointer += (_, e) =>
            WirePointer(new ZwlrVirtualPointerV1Resource(client, manager.Version, e.Id), Seat.Seat.FromResource(e.Seat));
        manager.CreateVirtualPointerWithOutput += (_, e) =>
            WirePointer(new ZwlrVirtualPointerV1Resource(client, manager.Version, e.Id), Seat.Seat.FromResource(e.Seat));
    }

    private void WirePointer(ZwlrVirtualPointerV1Resource pointer, Seat.Seat? seat)
    {
        var sink = seat?.InputSink ?? _sink;
        if (seat is not null)
        {
            seat.AddVirtualDevice(SeatCapability.Pointer);
            pointer.Destroyed += (_, _) => seat.RemoveVirtualDevice(SeatCapability.Pointer);
        }

        pointer.Motion += (_, e) => sink?.PointerMotion(e.Time, e.Dx.ToDouble(), e.Dy.ToDouble());
        pointer.MotionAbsolute += (_, e) =>
        {
            if (e.XExtent != 0 && e.YExtent != 0)
            {
                sink?.PointerMotionAbsolute(e.Time, e.X, e.Y, e.XExtent, e.YExtent);
            }
        };
        pointer.Button += (_, e) => sink?.PointerButton(e.Time, e.Button, e.State == WlPointer.ButtonState.Pressed);
        pointer.Axis += (_, e) => sink?.PointerAxis(e.Time, (uint)e.Axis, e.Value.ToDouble());
        pointer.AxisSource += (_, e) => sink?.PointerAxisSource((uint)e.AxisSource);
        pointer.AxisStop += (_, e) => sink?.PointerAxisStop(e.Time, (uint)e.Axis);
        pointer.Frame += (_, _) => sink?.Frame();
    }
}
