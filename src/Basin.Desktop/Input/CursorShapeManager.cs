using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_cursor_shape_manager_v1</c> global. It reports shape requests on pointer devices and on tablet tool devices as events. Drawing the cursor stays with the consumer.
/// </summary>
public sealed class CursorShapeManager : IDisposable
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

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpCursorShapeManagerV1Resource(client, version, id);
        manager.GetPointer += (_, e) =>
        {
            var device = new WpCursorShapeDeviceV1Resource(client, manager.Version, e.CursorShapeDevice);
            device.SetShape += (_, se) =>
            {
                if (!IsKnownShape(device, se.Shape))
                {
                    return;
                }

                if (_seat is { } seat && !seat.Pointer.ValidateEnterSerial(se.Serial))
                {
                    return;
                }

                var shape = (CursorShape)se.Shape;
                ShapeRequested?.Invoke(shape);
                if (_theme is { } theme && theme.TryResolve(shape, Scale, out var image))
                {
                    CursorRequested?.Invoke(image);
                }
            };
        };
        manager.GetTabletToolV2 += (_, e) =>
        {
            var device = new WpCursorShapeDeviceV1Resource(client, manager.Version, e.CursorShapeDevice);
            var toolResource = e.TabletTool;
            device.SetShape += (_, se) =>
            {
                if (!IsKnownShape(device, se.Shape) ||
                    _tablets is null ||
                    toolResource is null ||
                    !_tablets.TryResolveTool(toolResource, out var tool, out var serial) ||
                    serial == 0 ||
                    serial != se.Serial)
                {
                    return;
                }

                var shape = (CursorShape)se.Shape;
                ToolShapeRequested?.Invoke(tool, shape);
                if (_theme is { } theme && theme.TryResolve(shape, Scale, out var image))
                {
                    ToolCursorRequested?.Invoke(tool, image);
                }
            };
        };
    }

    private static bool IsKnownShape(WpCursorShapeDeviceV1Resource device, WpCursorShapeDeviceV1.Shape shape)
    {
        if ((uint)shape == 0 || (uint)shape > HighestShape(device.Version))
        {
            device.PostError((uint)WpCursorShapeDeviceV1.Error.InvalidShape, "unknown cursor shape");
            return false;
        }

        return true;
    }

    private static uint HighestShape(uint version) =>
        version >= 2 ? (uint)CursorShape.AllResize : (uint)CursorShape.ZoomOut;
}
