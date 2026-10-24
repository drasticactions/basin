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
public sealed class VirtualPointerManager : IDisposable, IZwlrVirtualPointerManagerV1Handler
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

    void IZwlrVirtualPointerManagerV1Handler.CreateVirtualPointer(ZwlrVirtualPointerManagerV1Resource resource, in ZwlrVirtualPointerManagerV1Resource.CreateVirtualPointerEventArgs args) =>
        CreatePointer(resource, args.Id, Seat.Seat.FromResource(args.Seat), null);

    void IZwlrVirtualPointerManagerV1Handler.CreateVirtualPointerWithOutput(ZwlrVirtualPointerManagerV1Resource resource, in ZwlrVirtualPointerManagerV1Resource.CreateVirtualPointerWithOutputEventArgs args) =>
        CreatePointer(
            resource,
            args.Id,
            Seat.Seat.FromResource(args.Seat),
            args.Output is { } output ? OutputGlobal.FromResource(output)?.Output : null);

    void IZwlrVirtualPointerManagerV1Handler.Destroy(ZwlrVirtualPointerManagerV1Resource resource)
    {
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ZwlrVirtualPointerManagerV1Resource(client, version, id, this);

    private void CreatePointer(ZwlrVirtualPointerManagerV1Resource manager, uint id, Seat.Seat? seat, IOutput? output)
    {
        var sink = seat?.InputSink ?? _sink;
        var pointer = new ZwlrVirtualPointerV1Resource(manager.Client, manager.Version, id, new VirtualPointer(sink, output, _layout));
        if (seat is not null)
        {
            seat.AddVirtualDevice(SeatCapability.Pointer);
            pointer.Destroyed += (_, _) => seat.RemoveVirtualDevice(SeatCapability.Pointer);
        }
    }

    private sealed class VirtualPointer(IInputSink? sink, IOutput? output, OutputLayout? layout) : IZwlrVirtualPointerV1Handler
    {
        void IZwlrVirtualPointerV1Handler.Motion(ZwlrVirtualPointerV1Resource resource, in ZwlrVirtualPointerV1Resource.MotionEventArgs args) =>
            sink?.PointerMotion(args.Time, args.Dx.ToDouble(), args.Dy.ToDouble());

        void IZwlrVirtualPointerV1Handler.MotionAbsolute(ZwlrVirtualPointerV1Resource resource, in ZwlrVirtualPointerV1Resource.MotionAbsoluteEventArgs args)
        {
            if (args.XExtent == 0 || args.YExtent == 0)
            {
                return;
            }

            if (output is not null && layout is { } withLayout &&
                withLayout.BoxOf(output) is { IsEmpty: false } box && withLayout.Bounds is { IsEmpty: false } bounds)
            {
                var layoutX = box.X + ((double)args.X / args.XExtent * box.Width);
                var layoutY = box.Y + ((double)args.Y / args.YExtent * box.Height);
                sink?.PointerMotionAbsolute(args.Time, layoutX - bounds.X, layoutY - bounds.Y, bounds.Width, bounds.Height);
                return;
            }

            sink?.PointerMotionAbsolute(args.Time, args.X, args.Y, args.XExtent, args.YExtent);
        }

        void IZwlrVirtualPointerV1Handler.Button(ZwlrVirtualPointerV1Resource resource, in ZwlrVirtualPointerV1Resource.ButtonEventArgs args) =>
            sink?.PointerButton(args.Time, args.Button, args.State == WlPointer.ButtonState.Pressed);

        void IZwlrVirtualPointerV1Handler.Axis(ZwlrVirtualPointerV1Resource resource, in ZwlrVirtualPointerV1Resource.AxisEventArgs args) =>
            sink?.PointerAxis(args.Time, (uint)args.Axis, args.Value.ToDouble());

        void IZwlrVirtualPointerV1Handler.Frame(ZwlrVirtualPointerV1Resource resource) => sink?.Frame();

        void IZwlrVirtualPointerV1Handler.AxisSource(ZwlrVirtualPointerV1Resource resource, in ZwlrVirtualPointerV1Resource.AxisSourceEventArgs args) =>
            sink?.PointerAxisSource((uint)args.AxisSource);

        void IZwlrVirtualPointerV1Handler.AxisStop(ZwlrVirtualPointerV1Resource resource, in ZwlrVirtualPointerV1Resource.AxisStopEventArgs args) =>
            sink?.PointerAxisStop(args.Time, (uint)args.Axis);

        void IZwlrVirtualPointerV1Handler.AxisDiscrete(ZwlrVirtualPointerV1Resource resource, in ZwlrVirtualPointerV1Resource.AxisDiscreteEventArgs args)
        {
        }

        void IZwlrVirtualPointerV1Handler.Destroy(ZwlrVirtualPointerV1Resource resource)
        {
        }
    }
}
