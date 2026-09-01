using Basin.WindowManager;

namespace DeskbarWm;

internal static class TabStrip
{
    public static Rect Slot(int frameWidth, int tabHeight, int count, int index)
    {
        var width = Math.Max(frameWidth / Math.Max(count, 1), 40);
        return new Rect(Math.Min(index * width, Math.Max(frameWidth - width, 0)), 0, width, tabHeight);
    }

    public static Rect CloseRect(Rect slot, in TabMetrics metrics)
    {
        if (metrics.CloseRect.IsEmpty)
        {
            return Rect.Empty;
        }

        var offset = metrics.CloseRect.Y;
        var size = metrics.CloseRect.Width;
        return new Rect(slot.X + offset, offset, size, size);
    }

    public static Rect ZoomRect(Rect slot, in TabMetrics metrics)
    {
        if (!metrics.HasZoom || metrics.CloseRect.IsEmpty)
        {
            return Rect.Empty;
        }

        var offset = metrics.CloseRect.Y;
        var size = metrics.CloseRect.Width;
        return new Rect(slot.Right - offset - size, offset, size, size);
    }

    public static int SlotAt(int frameWidth, int tabHeight, int count, int x, int y)
    {
        if (y < 0 || y >= tabHeight || x < 0 || x >= frameWidth || count <= 0)
        {
            return -1;
        }

        var width = Math.Max(frameWidth / Math.Max(count, 1), 40);
        var index = x / width;
        return index < count ? index : count - 1;
    }
}
