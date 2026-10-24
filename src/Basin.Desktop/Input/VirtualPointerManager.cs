using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;
using Xkb;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwlr_virtual_pointer_manager_v1</c> global. Events go to the input sink of the requested seat, or to the default sink when the client names no seat.
/// A pointer made with an output maps its absolute motion onto that output's layout box, then passes it on in the layout-bounds extent that <see cref="IInputSink"/> reads.
/// </summary>
public sealed class VirtualPointerManager : IDisposable
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly IInputSink? _sink;
    private readonly OutputLayout? _layout;

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualPointerManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="sink">The default sink. When it is <see langword="null"/>, a pointer with no seat has no effect.</param>
    /// <param name="layout">The layout that an output-bound pointer maps onto, or <see langword="null"/> to treat every pointer as bound to the whole layout.</param>
    public VirtualPointerManager(WlServerDisplay display, IInputSink? sink, OutputLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _sink = sink;
        _layout = layout;
        _global = display.CreateGlobal(ZwlrVirtualPointerManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Removes the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwlrVirtualPointerManagerV1Resource(client, version, id);
        manager.CreateVirtualPointer += (_, e) =>
            WirePointer(new ZwlrVirtualPointerV1Resource(client, manager.Version, e.Id), Seat.Seat.FromResource(e.Seat), null);
        manager.CreateVirtualPointerWithOutput += (_, e) =>
            WirePointer(
                new ZwlrVirtualPointerV1Resource(client, manager.Version, e.Id),
                Seat.Seat.FromResource(e.Seat),
                e.Output is { } output ? OutputGlobal.FromResource(output)?.Output : null);
    }

    private void WirePointer(ZwlrVirtualPointerV1Resource pointer, Seat.Seat? seat, IOutput? output)
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
            if (e.XExtent == 0 || e.YExtent == 0)
            {
                return;
            }

            if (output is not null && _layout is { } layout &&
                layout.BoxOf(output) is { IsEmpty: false } box && layout.Bounds is { IsEmpty: false } bounds)
            {
                var layoutX = box.X + ((double)e.X / e.XExtent * box.Width);
                var layoutY = box.Y + ((double)e.Y / e.YExtent * box.Height);
                sink?.PointerMotionAbsolute(e.Time, layoutX - bounds.X, layoutY - bounds.Y, bounds.Width, bounds.Height);
                return;
            }

            sink?.PointerMotionAbsolute(e.Time, e.X, e.Y, e.XExtent, e.YExtent);
        };
        pointer.Button += (_, e) => sink?.PointerButton(e.Time, e.Button, e.State == WlPointer.ButtonState.Pressed);
        pointer.Axis += (_, e) => sink?.PointerAxis(e.Time, (uint)e.Axis, e.Value.ToDouble());
        pointer.AxisSource += (_, e) => sink?.PointerAxisSource((uint)e.AxisSource);
        pointer.AxisStop += (_, e) => sink?.PointerAxisStop(e.Time, (uint)e.Axis);
        pointer.Frame += (_, _) => sink?.Frame();
    }
}
