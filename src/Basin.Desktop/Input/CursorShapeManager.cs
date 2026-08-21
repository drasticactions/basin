using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class CursorShapeManager : IDisposable
{
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly ICursorTheme? _theme;
    private readonly Basin.Seat.Seat? _seat;

    public CursorShapeManager(WlServerDisplay display, ICursorTheme? theme, Basin.Seat.Seat? seat = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _theme = theme;
        _seat = seat;
        _global = display.CreateGlobal(WpCursorShapeManagerV1.Interface, Version, OnBind);
    }

    public double Scale { get; set; } = 1;

    public event Action<CursorImage>? CursorRequested;

    public event Action<CursorShape>? ShapeRequested;

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
