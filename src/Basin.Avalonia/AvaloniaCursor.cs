using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Basin.Capabilities;

namespace Basin.Avalonia;

public static class AvaloniaCursor
{
    private static readonly Dictionary<StandardCursorType, Cursor> Cache = [];

    public static Cursor For(CursorShape shape)
    {
        var standard = Map(shape);
        if (!Cache.TryGetValue(standard, out var cursor))
        {
            Cache[standard] = cursor = OperatingSystem.IsMacOS() && MacFrameCursor.For(standard) is { } native
                ? native
                : new Cursor(standard);
        }

        return cursor;
    }

    public static Cursor? FromSurface(Surface surface, int hotspotX, int hotspotY)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.Current.Buffer is not { } buffer ||
            !buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return null;
        }

        try
        {
            var width = surface.Current.Width > 0 ? surface.Current.Width : buffer.Width;
            var height = surface.Current.Height > 0 ? surface.Current.Height : buffer.Height;
            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using (var frame = bitmap.Lock())
            {
                unsafe
                {
                    if (width == buffer.Width && height == buffer.Height)
                    {
                        for (var y = 0; y < buffer.Height; y++)
                        {
                            System.Buffer.MemoryCopy(
                                (void*)(view.Data + y * view.Stride),
                                (void*)(frame.Address + y * frame.RowBytes),
                                frame.RowBytes,
                                Math.Min(view.Stride, frame.RowBytes));
                        }
                    }
                    else
                    {
                        Resample(
                            view.Data, view.Stride, buffer.Width, buffer.Height,
                            frame.Address, frame.RowBytes, width, height);
                    }
                }
            }

            return new Cursor(bitmap, new PixelPoint(hotspotX, hotspotY));
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    private static unsafe void Resample(
        nint source, int sourceStride, int sourceWidth, int sourceHeight,
        nint target, int targetStride, int width, int height)
    {
        var stepX = (double)sourceWidth / width;
        var stepY = (double)sourceHeight / height;
        for (var y = 0; y < height; y++)
        {
            var sourceY = ((y + 0.5) * stepY) - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(sourceY), 0, sourceHeight - 1);
            var y1 = Math.Clamp(y0 + 1, 0, sourceHeight - 1);
            var fy = Math.Clamp(sourceY - y0, 0, 1);
            var row = (byte*)(target + (y * targetStride));
            var top = (byte*)(source + (y0 * sourceStride));
            var bottom = (byte*)(source + (y1 * sourceStride));
            for (var x = 0; x < width; x++)
            {
                var sourceX = ((x + 0.5) * stepX) - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(sourceX), 0, sourceWidth - 1);
                var x1 = Math.Clamp(x0 + 1, 0, sourceWidth - 1);
                var fx = Math.Clamp(sourceX - x0, 0, 1);
                for (var channel = 0; channel < 4; channel++)
                {
                    var topLeft = top[(x0 * 4) + channel];
                    var topRight = top[(x1 * 4) + channel];
                    var bottomLeft = bottom[(x0 * 4) + channel];
                    var bottomRight = bottom[(x1 * 4) + channel];
                    var above = topLeft + ((topRight - topLeft) * fx);
                    var below = bottomLeft + ((bottomRight - bottomLeft) * fx);
                    row[(x * 4) + channel] = (byte)Math.Clamp(Math.Round(above + ((below - above) * fy)), 0, 255);
                }
            }
        }
    }

    private static StandardCursorType Map(CursorShape shape) => shape switch
    {
        CursorShape.Default => StandardCursorType.Arrow,
        CursorShape.ContextMenu => StandardCursorType.Arrow,
        CursorShape.Help => StandardCursorType.Help,
        CursorShape.Pointer => StandardCursorType.Hand,
        CursorShape.Progress => StandardCursorType.AppStarting,
        CursorShape.Wait => StandardCursorType.Wait,
        CursorShape.Cell => StandardCursorType.Cross,
        CursorShape.Crosshair => StandardCursorType.Cross,
        CursorShape.Text => StandardCursorType.Ibeam,
        CursorShape.VerticalText => StandardCursorType.Ibeam,
        CursorShape.Alias => StandardCursorType.DragLink,
        CursorShape.Copy => StandardCursorType.DragCopy,
        CursorShape.Move => StandardCursorType.DragMove,
        CursorShape.NoDrop => StandardCursorType.No,
        CursorShape.NotAllowed => StandardCursorType.No,
        CursorShape.Grab => StandardCursorType.Hand,
        CursorShape.Grabbing => StandardCursorType.Hand,
        CursorShape.EResize => StandardCursorType.RightSide,
        CursorShape.NResize => StandardCursorType.TopSide,
        CursorShape.NeResize => StandardCursorType.TopRightCorner,
        CursorShape.NwResize => StandardCursorType.TopLeftCorner,
        CursorShape.SResize => StandardCursorType.BottomSide,
        CursorShape.SeResize => StandardCursorType.BottomRightCorner,
        CursorShape.SwResize => StandardCursorType.BottomLeftCorner,
        CursorShape.WResize => StandardCursorType.LeftSide,
        CursorShape.EwResize => StandardCursorType.SizeWestEast,
        CursorShape.NsResize => StandardCursorType.SizeNorthSouth,
        CursorShape.NeswResize => StandardCursorType.TopRightCorner,
        CursorShape.NwseResize => StandardCursorType.TopLeftCorner,
        CursorShape.ColResize => StandardCursorType.SizeWestEast,
        CursorShape.RowResize => StandardCursorType.SizeNorthSouth,
        CursorShape.AllScroll => StandardCursorType.SizeAll,
        _ => StandardCursorType.Arrow,
    };
}
