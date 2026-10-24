using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_cursor_shape_manager_v1</c> global. It reports pointer shape requests as events. Shape requests on tablet tool devices are accepted and ignored.
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

    /// <summary>
    /// Initializes a new instance of the <see cref="CursorShapeManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="theme">The theme that resolves shapes into images. When it is <see langword="null"/>, <see cref="CursorRequested"/> never occurs.</param>
    /// <param name="seat">The seat whose enter serial a request must match. When it is <see langword="null"/>, the serial is not checked.</param>
    public CursorShapeManager(WlServerDisplay display, ICursorTheme? theme, Basin.Seat.Seat? seat = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _theme = theme;
        _seat = seat;
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
                if ((uint)se.Shape == 0 || (uint)se.Shape > HighestShape(device.Version))
                {
                    device.PostError((uint)WpCursorShapeDeviceV1.Error.InvalidShape, "unknown cursor shape");
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
            _ = new WpCursorShapeDeviceV1Resource(client, manager.Version, e.CursorShapeDevice);
        };
    }

    private static uint HighestShape(uint version) =>
        version >= 2 ? (uint)CursorShape.AllResize : (uint)CursorShape.ZoomOut;
}
