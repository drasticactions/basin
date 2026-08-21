using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Basin.UI.Avalonia;

internal sealed class BasinCursorFactory : ICursorFactory
{
    private readonly Dictionary<StandardCursorType, BasinCursor> _cache = [];

    public ICursorImpl GetCursor(StandardCursorType cursorType)
    {
        if (_cache.TryGetValue(cursorType, out var cursor))
        {
            return cursor;
        }

        cursor = new BasinCursor(NameOf(cursorType));
        _cache[cursorType] = cursor;
        return cursor;
    }

    public ICursorImpl CreateCursor(Bitmap cursor, PixelPoint hotSpot) => new BasinCursor("default");

    private static string NameOf(StandardCursorType type) => type switch
    {
        StandardCursorType.Arrow => "default",
        StandardCursorType.Ibeam => "text",
        StandardCursorType.Wait => "wait",
        StandardCursorType.Cross => "crosshair",
        StandardCursorType.UpArrow => "up-arrow",
        StandardCursorType.SizeWestEast => "ew-resize",
        StandardCursorType.SizeNorthSouth => "ns-resize",
        StandardCursorType.SizeAll => "all-scroll",
        StandardCursorType.No => "not-allowed",
        StandardCursorType.Hand => "pointer",
        StandardCursorType.AppStarting => "progress",
        StandardCursorType.Help => "help",
        StandardCursorType.TopSide => "n-resize",
        StandardCursorType.BottomSide => "s-resize",
        StandardCursorType.LeftSide => "w-resize",
        StandardCursorType.RightSide => "e-resize",
        StandardCursorType.TopLeftCorner => "nw-resize",
        StandardCursorType.TopRightCorner => "ne-resize",
        StandardCursorType.BottomLeftCorner => "sw-resize",
        StandardCursorType.BottomRightCorner => "se-resize",
        StandardCursorType.DragMove => "grabbing",
        StandardCursorType.DragCopy => "copy",
        StandardCursorType.DragLink => "alias",
        StandardCursorType.None => "none",
        _ => "default",
    };
}
