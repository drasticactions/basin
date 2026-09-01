using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace Dinghy;

internal sealed class MenuSurface : IDisposable
{
    private const int MenuBorder = 1;
    private const int ItemPaddingX = 8;
    private const int ItemPaddingY = 4;
    private const int IconSize = 10;
    private const int IconGap = 6;
    private const int DiamondSize = 8;
    private const int FlatShadowSize = 3;
    private const uint FlatShadowColor = 0x404040FF;
    private const uint BorderColor = 0x000000FF;

    private readonly ManagerSurface _surface;
    private string? _lastKey;

    internal MenuSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm,
        IReadOnlyList<MenuItemEntry> items,
        string? headerTitle,
        int scale)
    {
        Output = output;
        Items = items;
        HeaderTitle = headerTitle;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Overlay, "dinghy-window-menu");
        Measure(scale);
        _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Left);
        _surface.SetSize(SurfaceSize.Width, SurfaceSize.Height);
        _surface.Configured += wm.RequestManage;
    }

    public void ApplyPosition()
    {
        _surface.SetMargin(Origin.Y, Origin.X);
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public IReadOnlyList<MenuItemEntry> Items { get; }

    public string? HeaderTitle { get; }

    public int? Hovered { get; private set; }

    public Point Origin { get; set; }

    public Size SurfaceSize { get; private set; }

    public uint SurfaceId => _surface.SurfaceId;

    private static int ItemHeight => (int)Math.Ceiling(Theme.FontSize) + (ItemPaddingY * 2);

    private static int ShadowLeft => Theme.ShadowsEnabled ? Theme.ShadowsActiveSize : 0;

    private static int ShadowTop => Theme.ShadowsEnabled ? Theme.ShadowsActiveSize / 2 : 0;

    private static int ShadowRight => Theme.ShadowsEnabled ? Theme.ShadowsActiveSize : FlatShadowSize;

    private static int ShadowBottom => Theme.ShadowsEnabled
        ? Theme.ShadowsActiveSize + (Theme.ShadowsActiveSize / 2)
        : FlatShadowSize;

    public Point PointerAnchor => new(
        ShadowLeft + (ItemHeight / 2),
        ShadowTop + MenuBorder + (ItemHeight / 2));

    public int? ItemAt(int x, int y)
    {
        var contentX = ShadowLeft + MenuBorder;
        var contentY = ShadowTop + MenuBorder;
        var contentWidth = MenuWidth - (MenuBorder * 2);
        var contentHeight = MenuHeight - (MenuBorder * 2);
        if (x < contentX || y < contentY || x >= contentX + contentWidth || y >= contentY + contentHeight)
        {
            return null;
        }

        var headerHeight = HeaderTitle is null ? 0 : ItemHeight;
        if (y < contentY + headerHeight)
        {
            return null;
        }

        var index = (y - contentY - headerHeight) / ItemHeight;
        return index < Items.Count ? index : null;
    }

    public bool UpdateHover(int x, int y)
    {
        var next = ItemAt(x, y);
        if (next != Hovered)
        {
            Hovered = next;
            return true;
        }

        return false;
    }

    public bool SelectNext()
    {
        if (Items.Count == 0)
        {
            return false;
        }

        var next = Hovered is { } index ? (index + 1) % Items.Count : 0;
        if (Hovered != next)
        {
            Hovered = next;
            return true;
        }

        return false;
    }

    public void SelectWindow(ManagedWindow? window)
    {
        Hovered = null;
        for (var i = 0; i < Items.Count; i++)
        {
            if (ReferenceEquals(Items[i].Window, window))
            {
                Hovered = i;
                return;
            }
        }
    }

    public bool Render(int scale)
    {
        if (!_surface.IsConfigured)
        {
            return false;
        }

        var key = $"{scale}|{Hovered}";
        if (key == _lastKey)
        {
            return false;
        }

        var pixels = _surface.Prepare(SurfaceSize.Width, SurfaceSize.Height, scale);
        if (pixels == 0)
        {
            return false;
        }

        var menuWidth = MenuWidth;
        var menuHeight = MenuHeight;
        if (Theme.ShadowsEnabled)
        {
            ShadowNineSlice.Draw(
                _surface.Bytes,
                SurfaceSize.Width * scale,
                menuWidth,
                menuHeight - (Theme.ShadowsActiveSize / 2),
                Theme.ShadowsActiveSize,
                cornerRadius: 0,
                Theme.ShadowsColor,
                scale);
        }

        using var surface = _surface.CreateCanvas(pixels);
        if (surface is null)
        {
            return false;
        }

        Draw(surface.Canvas, menuWidth, menuHeight, scale);
        surface.Canvas.Flush();
        _surface.SetInputRegion(new Rect(ShadowLeft, ShadowTop, menuWidth, menuHeight));
        _lastKey = key;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Dispose() => _surface.Dispose();

    private int MenuWidth => SurfaceSize.Width - ShadowLeft - ShadowRight;

    private int MenuHeight => SurfaceSize.Height - ShadowTop - ShadowBottom;

    private void Measure(int scale)
    {
        scale = Math.Max(scale, 1);
        using var font = new SKFont(Fonts.Sans, Theme.FontSize * scale);
        var maxWidth = 0f;
        foreach (var item in Items)
        {
            maxWidth = Math.Max(maxWidth, font.MeasureText(item.Title) / scale);
        }

        if (HeaderTitle is { } header)
        {
            maxWidth = Math.Max(maxWidth, font.MeasureText(header) / scale);
        }

        var rows = Items.Count + (HeaderTitle is null ? 0 : 1);
        var contentWidth = (ItemPaddingX * 2) + IconSize + IconGap + (int)Math.Ceiling(maxWidth);
        var menuWidth = contentWidth + (MenuBorder * 2);
        var menuHeight = (rows * ItemHeight) + (MenuBorder * 2);
        SurfaceSize = new Size(
            menuWidth + ShadowLeft + ShadowRight,
            menuHeight + ShadowTop + ShadowBottom);
    }

    private void Draw(SKCanvas canvas, int menuWidth, int menuHeight, int scale)
    {
        var left = ShadowLeft * scale;
        var top = ShadowTop * scale;
        using var paint = new SKPaint();
        paint.IsAntialias = false;

        if (!Theme.ShadowsEnabled)
        {
            paint.Color = Theme.Color(FlatShadowColor);
            canvas.DrawRect(left + (FlatShadowSize * scale), top + (FlatShadowSize * scale),
                menuWidth * scale, menuHeight * scale, paint);
        }

        paint.Color = Theme.Color(Theme.MenuBg);
        canvas.DrawRect(left, top, menuWidth * scale, menuHeight * scale, paint);

        paint.Color = Theme.Color(BorderColor);
        var border = MenuBorder * scale;
        canvas.DrawRect(left, top, menuWidth * scale, border, paint);
        canvas.DrawRect(left, top + (menuHeight * scale) - border, menuWidth * scale, border, paint);
        canvas.DrawRect(left, top, border, menuHeight * scale, paint);
        canvas.DrawRect(left + (menuWidth * scale) - border, top, border, menuHeight * scale, paint);

        var headerHeight = HeaderTitle is null ? 0 : ItemHeight;
        if (HeaderTitle is { } header)
        {
            DrawRow(canvas, header, MenuBorder, menuWidth, scale,
                Theme.TitlebarBgActive, Theme.TitlebarTextActive, hidden: false, active: false);
        }

        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var selected = Hovered == i;
            DrawRow(
                canvas,
                item.Title,
                MenuBorder + headerHeight + (i * ItemHeight),
                menuWidth,
                scale,
                selected ? Theme.MenuHighlightBg : Theme.MenuBg,
                selected ? Theme.MenuHighlightText : Theme.MenuText,
                item.Hidden,
                item.Active);
        }
    }

    private void DrawRow(
        SKCanvas canvas,
        string title,
        int rowY,
        int menuWidth,
        int scale,
        uint background,
        uint text,
        bool hidden,
        bool active)
    {
        var left = ShadowLeft * scale;
        var top = ShadowTop * scale;
        using var paint = new SKPaint();
        paint.IsAntialias = false;
        paint.Color = Theme.Color(background);
        canvas.DrawRect(
            left + (MenuBorder * scale),
            top + (rowY * scale),
            (menuWidth - (MenuBorder * 2)) * scale,
            ItemHeight * scale,
            paint);

        var startX = MenuBorder + ItemPaddingX;
        paint.Color = Theme.Color(text);
        if (hidden)
        {
            var x = left + (startX * scale);
            var y = top + ((rowY + ((ItemHeight - IconSize) / 2)) * scale);
            var size = IconSize * scale;
            var dash = 2 * scale;
            for (var offset = 0; offset < size; offset += dash * 2)
            {
                var run = Math.Min(dash, size - offset);
                canvas.DrawRect(x + offset, y, run, scale, paint);
                canvas.DrawRect(x + offset, y + size - scale, run, scale, paint);
                canvas.DrawRect(x, y + offset, scale, run, paint);
                canvas.DrawRect(x + size - scale, y + offset, scale, run, paint);
            }
        }

        if (active)
        {
            var cx = left + ((startX + (IconSize / 2)) * scale);
            var cy = top + ((rowY + (ItemHeight / 2)) * scale);
            var half = DiamondSize * scale / 2;
            using var builder = new SKPathBuilder();
            builder.MoveTo(cx, cy - half);
            builder.LineTo(cx + half, cy);
            builder.LineTo(cx, cy + half);
            builder.LineTo(cx - half, cy);
            builder.Close();
            using var diamond = builder.Detach();
            paint.IsAntialias = true;
            canvas.DrawPath(diamond, paint);
            paint.IsAntialias = false;
        }

        using var font = new SKFont(Fonts.Sans, Theme.FontSize * scale);
        var textX = left + ((startX + IconSize + IconGap) * scale);
        var available = left + ((menuWidth - MenuBorder - ItemPaddingX) * scale) - textX;
        var label = Fonts.Ellipsize(font, title, available);
        var metrics = font.Metrics;
        var baseline = top + (rowY * scale)
            + (((ItemHeight * scale) - (metrics.Descent - metrics.Ascent)) / 2f) - metrics.Ascent;
        paint.IsAntialias = true;
        canvas.DrawText(label, textX, baseline, SKTextAlign.Left, font, paint);
    }
}
