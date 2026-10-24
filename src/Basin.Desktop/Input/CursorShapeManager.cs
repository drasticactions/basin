using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_cursor_shape_manager_v1</c> global. It reports shape requests on pointer devices and on tablet tool devices as events. Drawing the cursor stays with the consumer.
/// </summary>
public sealed class CursorShapeManager : IDisposable, IWpCursorShapeManagerV1Handler, IWpCursorShapeDeviceV1Handler
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly ICursorTheme? _theme;
    private readonly Basin.Seat.Seat? _seat;
    private readonly TabletManager? _tablets;

    /// <summary>
    /// Initializes a new instance of the <see cref="CursorShapeManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="theme">The theme that resolves shapes into images. When it is <see langword="null"/>, <see cref="CursorRequested"/> and <see cref="ToolCursorRequested"/> never occur.</param>
    /// <param name="seat">The seat whose enter serial a request must match. When it is <see langword="null"/>, the serial is not checked.</param>
    /// <param name="tablets">The tablet manager whose tools the tablet tool devices name. When it is <see langword="null"/>, <see cref="ToolShapeRequested"/> never occurs.</param>
    public CursorShapeManager(WlServerDisplay display, ICursorTheme? theme, Basin.Seat.Seat? seat = null, TabletManager? tablets = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _theme = theme;
        _seat = seat;
        _tablets = tablets;
        _global = display.CreateGlobal(WpCursorShapeManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets or sets the scale at which the theme resolves cursor images.
    /// </summary>
    public double Scale { get; set; } = 1;

    /// <summary>
    /// Occurs after <see cref="ShapeRequested"/> when the theme resolves the shape into an image.
    /// </summary>
    public event Action<CursorImage>? CursorRequested;

    /// <summary>
    /// Occurs when a client with a valid enter serial sets a shape on its pointer device. An unknown shape is a protocol error instead.
    /// </summary>
    public event Action<CursorShape>? ShapeRequested;

    /// <summary>
    /// Occurs when a client sets a shape on a tablet tool device with the latest <c>proximity_in</c> serial of that tool. A request with an old serial is ignored.
    /// </summary>
    public event Action<TabletManager.TabletTool, CursorShape>? ToolShapeRequested;

    /// <summary>
    /// Occurs after <see cref="ToolShapeRequested"/> when the theme resolves the shape into an image.
    /// </summary>
    public event Action<TabletManager.TabletTool, CursorImage>? ToolCursorRequested;

    /// <summary>
    /// Removes the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    void IWpCursorShapeManagerV1Handler.Destroy(WpCursorShapeManagerV1Resource resource)
    {
    }

    void IWpCursorShapeManagerV1Handler.GetPointer(WpCursorShapeManagerV1Resource resource, in WpCursorShapeManagerV1Resource.GetPointerEventArgs args) =>
        _ = new WpCursorShapeDeviceV1Resource(resource.Client, resource.Version, args.CursorShapeDevice, this);

    void IWpCursorShapeManagerV1Handler.GetTabletToolV2(WpCursorShapeManagerV1Resource resource, in WpCursorShapeManagerV1Resource.GetTabletToolV2EventArgs args) =>
        _ = new WpCursorShapeDeviceV1Resource(resource.Client, resource.Version, args.CursorShapeDevice, new ToolDevice(this, args.TabletTool));

    void IWpCursorShapeDeviceV1Handler.Destroy(WpCursorShapeDeviceV1Resource resource)
    {
    }

    void IWpCursorShapeDeviceV1Handler.SetShape(WpCursorShapeDeviceV1Resource resource, in WpCursorShapeDeviceV1Resource.SetShapeEventArgs args)
    {
        if (!IsKnownShape(resource, args.Shape))
        {
            return;
        }

        if (_seat is { } seat && !seat.Pointer.ValidateEnterSerial(args.Serial))
        {
            return;
        }

        var shape = (CursorShape)args.Shape;
        ShapeRequested?.Invoke(shape);
        if (_theme is { } theme && theme.TryResolve(shape, Scale, out var image))
        {
            CursorRequested?.Invoke(image);
        }
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new WpCursorShapeManagerV1Resource(client, version, id, this);

    private void OnToolSetShape(WpCursorShapeDeviceV1Resource device, ZwpTabletToolV2Resource? toolResource, in WpCursorShapeDeviceV1Resource.SetShapeEventArgs args)
    {
        if (!IsKnownShape(device, args.Shape) ||
            _tablets is null ||
            toolResource is null ||
            !_tablets.TryResolveTool(toolResource, out var tool, out var serial) ||
            serial == 0 ||
            serial != args.Serial)
        {
            return;
        }

        var shape = (CursorShape)args.Shape;
        ToolShapeRequested?.Invoke(tool, shape);
        if (_theme is { } theme && theme.TryResolve(shape, Scale, out var image))
        {
            ToolCursorRequested?.Invoke(tool, image);
        }
    }

    private static bool IsKnownShape(WpCursorShapeDeviceV1Resource device, WpCursorShapeDeviceV1.Shape shape)
    {
        if ((uint)shape == 0 || (uint)shape > HighestShape(device.Version))
        {
            device.PostError((uint)WpCursorShapeDeviceV1.Error.InvalidShape, "The cursor shape is not known.");
            return false;
        }

        return true;
    }

    private static uint HighestShape(uint version) =>
        version >= 2 ? (uint)CursorShape.AllResize : (uint)CursorShape.ZoomOut;

    private sealed class ToolDevice(CursorShapeManager owner, ZwpTabletToolV2Resource? toolResource) : IWpCursorShapeDeviceV1Handler
    {
        void IWpCursorShapeDeviceV1Handler.Destroy(WpCursorShapeDeviceV1Resource resource)
        {
        }

        void IWpCursorShapeDeviceV1Handler.SetShape(WpCursorShapeDeviceV1Resource resource, in WpCursorShapeDeviceV1Resource.SetShapeEventArgs args) =>
            owner.OnToolSetShape(resource, toolResource, in args);
    }
}
