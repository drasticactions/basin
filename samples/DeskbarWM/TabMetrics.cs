using Basin.WindowManager;
using SkiaSharp;

namespace DeskbarWm;

internal readonly struct TabMetrics
{
    public int BorderWidth { get; private init; }

    public int TabHeight { get; private init; }

    public Rect TabRect { get; private init; }

    public Rect CloseRect { get; private init; }

    public Rect ZoomRect { get; private init; }

    public int TextOffset { get; private init; }

    public int FrameWidth { get; private init; }

    public int FrameHeight { get; private init; }

    public bool HasZoom { get; private init; }

    public int ButtonInset { get; private init; }

    public static TabMetrics Compute(
        int contentWidth,
        int contentHeight,
        WindowFeel feel,
        string? title,
        SKFont font,
        float tabLocation,
        bool closable,
        bool zoomable)
    {
        var floating = feel == WindowFeel.Floating;
        var borderWidth = floating ? 3 : 5;
        var scaleFactor = MathF.Max(font.Size / 12f, 1f);
        borderWidth = (int)(borderWidth * scaleFactor);

        var metrics = font.Metrics;
        var ascent = -metrics.Ascent;
        var descent = metrics.Descent;
        var spacing = borderWidth * 1.4f;
        var tabHeight = (int)MathF.Ceiling(ascent + descent + spacing);
        if (floating)
        {
            tabHeight = Math.Max(tabHeight - 4, 4);
        }

        var frameWidth = contentWidth + (borderWidth * 2);
        var frameHeight = contentHeight + (borderWidth * 2) + tabHeight;

        var offset = (int)MathF.Floor(font.Size / (floating ? 2.6f : 2.3f));
        var inset = (int)MathF.Floor(font.Size / (floating ? 5.0f : 6.0f));
        var size = tabHeight - (2 * offset) + inset;
        var textOffset = (int)(borderWidth * (floating ? 3.4f : 3.6f));

        var minTabSize = (inset * 2) + textOffset;
        if (closable)
        {
            minTabSize += offset + size;
        }

        if (zoomable)
        {
            minTabSize += offset + size;
        }

        var titleWidth = title is { Length: > 0 } ? (int)MathF.Ceiling(font.MeasureText(title)) : 0;
        var maxTabSize = minTabSize + (titleWidth > 0 ? titleWidth + textOffset : 0);

        var tabWidth = Math.Clamp(frameWidth, minTabSize, Math.Max(maxTabSize, minTabSize));
        var maxOffset = Math.Max(frameWidth - tabWidth, 0);
        var tabOffset = Math.Clamp((int)MathF.Round(tabLocation * maxOffset), 0, maxOffset);

        var tabRect = new Rect(tabOffset, 0, tabWidth, tabHeight);
        var closeRect = closable
            ? new Rect(tabRect.X + offset, offset, size, size)
            : Rect.Empty;
        var zoomRect = zoomable
            ? new Rect(tabRect.Right - offset - size, offset, size, size)
            : Rect.Empty;

        return new TabMetrics
        {
            BorderWidth = borderWidth,
            TabHeight = tabHeight,
            TabRect = tabRect,
            CloseRect = closeRect,
            ZoomRect = zoomRect,
            TextOffset = textOffset,
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            HasZoom = zoomable,
            ButtonInset = inset,
        };
    }

    public DecorationHit HitTest(int localX, int localY, int contentWidth, int contentHeight)
    {
        var point = new Point(localX, localY);
        if (TabRect.Contains(point))
        {
            if (CloseRect.Contains(point))
            {
                return new DecorationHit(FramePart.CloseBox, Edges.None);
            }

            if (ZoomRect.Contains(point))
            {
                return new DecorationHit(FramePart.ZoomBox, Edges.None);
            }

            return new DecorationHit(FramePart.Tab, Edges.None);
        }

        var bw = BorderWidth;
        var contentX = bw;
        var contentY = TabHeight + bw;
        if (localX >= contentX && localX < contentX + contentWidth
            && localY >= contentY && localY < contentY + contentHeight)
        {
            return DecorationHit.None;
        }

        if (localY < TabHeight || localX < 0 || localX >= FrameWidth || localY >= FrameHeight)
        {
            return DecorationHit.None;
        }

        if (localX >= FrameWidth - BorderResizeReach && localY >= FrameHeight - BorderResizeReach)
        {
            return new DecorationHit(FramePart.ResizeCorner, Edges.Right | Edges.Bottom);
        }

        var edges = Edges.None;
        if (localX < contentX)
        {
            edges |= Edges.Left;
        }
        else if (localX >= contentX + contentWidth)
        {
            edges |= Edges.Right;
        }

        if (localY < contentY)
        {
            edges |= Edges.Top;
        }
        else if (localY >= contentY + contentHeight)
        {
            edges |= Edges.Bottom;
        }

        return edges == Edges.None ? DecorationHit.None : new DecorationHit(FramePart.Border, edges);
    }

    private int BorderResizeReach => Math.Max(Theme.BorderResizeLength, BorderWidth);
}
